namespace Clawbot.Agents.Core.Skills;

// Marker interface for all ClawBot skills.
// Each skill exposes a narrow capability used by one or more agents.
// Skills declared in .sdd/skills/<name>.md must point to a concrete adapter implementing
// one of the I*Skill interfaces under this namespace.
public interface ISkill
{
    string Name { get; }
}
