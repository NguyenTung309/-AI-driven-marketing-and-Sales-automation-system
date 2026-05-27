using Clawbot.Domain.Conversations;

namespace Clawbot.Application.Abstractions;

public interface IConversationSet
{
    void Add(Conversation conversation);
}
