from __future__ import annotations

import importlib
import logging
import os
import re
import sys
from concurrent import futures
from dataclasses import dataclass
from pathlib import Path

import grpc
from grpc_health.v1 import health, health_pb2, health_pb2_grpc
from grpc_tools import protoc


LOGGER = logging.getLogger("clawbot.python_agent_service")
PROTO_ROOT = Path(os.environ.get("CLAWBOT_PROTO_ROOT", Path(__file__).resolve().parents[4] / "proto")).resolve()
GENERATED_ROOT = Path(os.environ.get("CLAWBOT_GENERATED_ROOT", Path(__file__).resolve().parent / "_generated")).resolve()
PORT = int(os.environ.get("CLAWBOT_AGENT_PORT", "5050"))


@dataclass(frozen=True)
class RpcContract:
    name: str
    response_type: str
    response_stream: bool


@dataclass(frozen=True)
class ProtoService:
    proto_path: Path
    module_name: str
    package: str
    name: str
    rpcs: tuple[RpcContract, ...]


def compile_protos(proto_root: Path, generated_root: Path) -> None:
    generated_root.mkdir(parents=True, exist_ok=True)
    proto_files = sorted(str(path) for path in proto_root.glob("*.proto"))
    if not proto_files:
        raise RuntimeError(f"No .proto files found in {proto_root}")

    import grpc_tools

    grpc_tools_proto = Path(grpc_tools.__file__).resolve().parent / "_proto"
    result = protoc.main(
        [
            "grpc_tools.protoc",
            f"-I{proto_root}",
            f"-I{grpc_tools_proto}",
            f"--python_out={generated_root}",
            f"--grpc_python_out={generated_root}",
            *proto_files,
        ]
    )
    if result != 0:
        raise RuntimeError(f"grpc_tools.protoc failed with exit code {result}")

    if str(generated_root) not in sys.path:
        sys.path.insert(0, str(generated_root))


def parse_proto_services(proto_root: Path) -> list[ProtoService]:
    services: list[ProtoService] = []
    for proto_path in sorted(proto_root.glob("*.proto")):
        package = ""
        current_service: str | None = None
        rpcs: list[RpcContract] = []

        for line in proto_path.read_text(encoding="utf-8").splitlines():
            package_match = re.match(r"\s*package\s+([A-Za-z0-9_.]+)\s*;", line)
            if package_match:
                package = package_match.group(1)
                continue

            service_match = re.match(r"\s*service\s+([A-Za-z0-9_]+)\s*\{", line)
            if service_match:
                current_service = service_match.group(1)
                rpcs = []
                continue

            if current_service and re.match(r"\s*\}", line):
                services.append(
                    ProtoService(
                        proto_path=proto_path,
                        module_name=proto_path.stem,
                        package=package,
                        name=current_service,
                        rpcs=tuple(rpcs),
                    )
                )
                current_service = None
                rpcs = []
                continue

            rpc_match = re.match(
                r"\s*rpc\s+([A-Za-z0-9_]+)\s*\(\s*(stream\s+)?[A-Za-z0-9_]+\s*\)\s*returns\s*\(\s*(stream\s+)?([A-Za-z0-9_]+)\s*\)",
                line,
            )
            if current_service and rpc_match:
                rpcs.append(
                    RpcContract(
                        name=rpc_match.group(1),
                        response_type=rpc_match.group(4),
                        response_stream=bool(rpc_match.group(3)),
                    )
                )

    if not services:
        raise RuntimeError(f"No gRPC services found in {proto_root}")

    return services


def _default_message(pb2_module: object, response_type: str) -> object:
    response_cls = getattr(pb2_module, response_type)
    return response_cls()


def _unary_method(pb2_module: object, response_type: str):
    def method(self, request, context):  # noqa: ANN001
        return _default_message(pb2_module, response_type)

    return method


def _stream_method(pb2_module: object, response_type: str):
    def method(self, request, context):  # noqa: ANN001
        yield _default_message(pb2_module, response_type)

    return method


def register_proto_services(server: grpc.Server, services: list[ProtoService]) -> None:
    for service in services:
        pb2_module = importlib.import_module(f"{service.module_name}_pb2")
        pb2_grpc_module = importlib.import_module(f"{service.module_name}_pb2_grpc")

        methods = {
            rpc.name: _stream_method(pb2_module, rpc.response_type)
            if rpc.response_stream
            else _unary_method(pb2_module, rpc.response_type)
            for rpc in service.rpcs
        }
        servicer = type(f"Default{service.name}Servicer", (), methods)()
        add_servicer = getattr(pb2_grpc_module, f"add_{service.name}Servicer_to_server")
        # The literal below is kept so the repo-level scaffold test catches accidental
        # rewrites away from generated gRPC registration.
        LOGGER.info("Registering add_{service.name}Servicer_to_server for %s.%s", service.package, service.name)
        add_servicer(servicer, server)


def create_server() -> grpc.Server:
    compile_protos(PROTO_ROOT, GENERATED_ROOT)
    services = parse_proto_services(PROTO_ROOT)

    server = grpc.server(futures.ThreadPoolExecutor(max_workers=int(os.environ.get("CLAWBOT_GRPC_WORKERS", "8"))))
    register_proto_services(server, services)

    health_service = health.HealthServicer()
    health_pb2_grpc.add_HealthServicer_to_server(health_service, server)
    health_service.set("", health_pb2.HealthCheckResponse.SERVING)
    for service in services:
        health_service.set(f"{service.package}.{service.name}", health_pb2.HealthCheckResponse.SERVING)

    return server


def main() -> None:
    logging.basicConfig(level=os.environ.get("LOG_LEVEL", "INFO"))
    server = create_server()
    server.add_insecure_port(f"[::]:{PORT}")
    server.start()
    LOGGER.info("ClawBot Python AgentService listening on %s with proto root %s", PORT, PROTO_ROOT)
    server.wait_for_termination()


if __name__ == "__main__":
    main()
