FROM python:3.12-slim

WORKDIR /app

COPY src/agents/Clawbot.PythonAgentService/requirements.txt ./requirements.txt
RUN pip install --no-cache-dir -r requirements.txt

COPY proto /proto
COPY src/agents/Clawbot.PythonAgentService/app ./app

ENV CLAWBOT_PROTO_ROOT=/proto
ENV CLAWBOT_AGENT_PORT=5050

EXPOSE 5050

CMD ["python", "-m", "app.main"]
