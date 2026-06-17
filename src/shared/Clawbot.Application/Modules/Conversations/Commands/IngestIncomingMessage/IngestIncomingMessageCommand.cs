using Clawbot.Application.Common;
using Clawbot.SharedKernel.Channels;
using MediatR;

namespace Clawbot.Application.Modules.Conversations.Commands.IngestIncomingMessage;

// Carries one already-parsed inbound channel message (HMAC already verified at the gateway).
public sealed record IngestIncomingMessageCommand(ChannelMessage Message) : IRequest<Result<Guid>>;
