namespace Clawbot.Application.Abstractions;

public interface IAppDbContext
{
    IConversationSet Conversations { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
