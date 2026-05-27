using Clawbot.Application.Common;
using MediatR;

namespace Clawbot.Application.Modules.Conversations.Commands.OpenConversation;

public sealed record OpenConversationCommand(string Channel, string ExternalThreadId) : IRequest<Result<Guid>>;
