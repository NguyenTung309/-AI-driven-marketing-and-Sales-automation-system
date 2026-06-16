using Clawbot.Application.Abstractions;
using Clawbot.Application.Common;
using Clawbot.Domain.Conversations;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using MediatR;

namespace Clawbot.Application.Modules.Conversations.Commands.IngestIncomingMessage;

public sealed class IngestIncomingMessageHandler(IAppDbContext db, ITenantAccessor tenants, IClock clock)
    : IRequestHandler<IngestIncomingMessageCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(IngestIncomingMessageCommand request, CancellationToken cancellationToken)
    {
        var tenant = tenants.Require();
        var msg = request.Message;
        var conversations = db.Conversations;

        // EARS[WHEN an inbound channel message arrives THE SYSTEM SHALL append it to the matching
        //      (platform, external_thread_id) conversation, creating one if none exists yet]
        var conversation = await conversations.FindByThreadAsync(msg.Channel, msg.ExternalThreadId, cancellationToken);
        if (conversation is null)
        {
            conversation = Conversation.Open(tenant.TenantId, msg.Channel, msg.ExternalThreadId, clock.UtcNow);
            conversations.Add(conversation);
        }

        conversation.AppendMessage("in", "contact", msg.Text, "text", msg.SentAt);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(conversation.Id);
    }
}
