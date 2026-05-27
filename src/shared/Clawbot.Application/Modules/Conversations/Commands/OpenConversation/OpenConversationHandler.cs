using Clawbot.Application.Abstractions;
using Clawbot.Application.Common;
using Clawbot.Domain.Conversations;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using MediatR;

namespace Clawbot.Application.Modules.Conversations.Commands.OpenConversation;

public sealed class OpenConversationHandler(IAppDbContext db, ITenantAccessor tenants, IClock clock)
    : IRequestHandler<OpenConversationCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(OpenConversationCommand request, CancellationToken cancellationToken)
    {
        var tenant = tenants.Require();
        var conversation = Conversation.Open(tenant.TenantId, request.Channel, request.ExternalThreadId, clock.UtcNow);
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success(conversation.Id);
    }
}
