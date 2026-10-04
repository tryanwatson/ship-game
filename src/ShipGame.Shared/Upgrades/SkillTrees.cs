using ShipGame.Shared.Abilities;
using static ShipGame.Shared.Abilities.AbilityStat;
using static ShipGame.Shared.Upgrades.SkillEffect;

namespace ShipGame.Shared.Upgrades;

/// <summary>
/// Every weapon's skill tree. Each has two mutually exclusive branches of three skills,
/// each with its own capstone, so a ship can own at most three of a tree's six. Add a skill by adding a row: its effects are
/// read by the ability (see <see cref="AbilityStat"/>), and the shipyard lays the tree out from the prerequisites.
/// Rows are listed tier by tier, left branch first.
/// </summary>
public static class SkillTrees
{
    public static readonly IReadOnlyList<SkillDefinition> All = new SkillDefinition[]
    {
        // Broadside: concentrated volleys or continuous pressure, independent of ship speed.
        new("heavy-volley", "HEAVY VOLLEY", BroadsideVolley.AbilityId, 20, "+2 CANNON PER VOLLEY. +20% RELOAD TIME.")
        {
            Excludes = new[] { "rapid-guns" },
            Effects = new[] { Flat(ShotCount, 2), Percent(Cooldown, 0.2f) },
        },
        new("rapid-guns", "RAPID GUNS", BroadsideVolley.AbilityId, 20, "1 FEWER CANNON PER VOLLEY. -30% RELOAD TIME.")
        {
            Excludes = new[] { "heavy-volley" },
            Effects = new[] { Flat(ShotCount, -1), Percent(Cooldown, -0.3f) },
        },
        new("point-blank", "POINT BLANK", BroadsideVolley.AbilityId, 35, "+50% DAMAGE TO SHIPS WITHIN HALF RANGE.")
        {
            Requires = new[] { "heavy-volley" },
            Effects = new[] { Flat(CloseRangeDamage, 0.5f) },
        },
        new("improved-powder", "IMPROVED POWDER", BroadsideVolley.AbilityId, 35, "+25% DAMAGE AT ANY SPEED.")
        {
            Requires = new[] { "rapid-guns" },
            Effects = new[] { Percent(Damage, 0.25f) },
        },
        new("thunderous-volley", "THUNDEROUS VOLLEY", BroadsideVolley.AbilityId, 60,
            "+2 MORE CANNON, +25% DAMAGE AND +20% RELOAD TIME.")
        {
            Requires = new[] { "point-blank" },
            Effects = new[] { Flat(ShotCount, 2), Percent(Damage, 0.25f), Percent(Cooldown, 0.2f) },
        },
        new("rolling-thunder", "ROLLING THUNDER", BroadsideVolley.AbilityId, 60, "+1 CANNON AND -20% RELOAD TIME.")
        {
            Requires = new[] { "improved-powder" },
            Effects = new[] { Flat(ShotCount, 1), Percent(Cooldown, -0.2f) },
        },

        // Long gun: precision at distance or penetration through formations.
        new("rifled-barrel", "RIFLED BARREL", LongGun.AbilityId, 20, "+40% SHOT SPEED AND +25% RANGE.")
        {
            Excludes = new[] { "heavy-shot" },
            Effects = new[] { Percent(ProjectileSpeed, 0.4f), Percent(AbilityStat.Range, 0.25f) },
        },
        new("heavy-shot", "HEAVY SHOT", LongGun.AbilityId, 20, "+50% DAMAGE. +20% RELOAD TIME.")
        {
            Excludes = new[] { "rifled-barrel" },
            Effects = new[] { Percent(Damage, 0.5f), Percent(Cooldown, 0.2f) },
        },
        new("rangefinder", "RANGEFINDER", LongGun.AbilityId, 35, "LONG RANGE HITS REFUND 35% OF THE FULL RELOAD.")
        {
            Requires = new[] { "rifled-barrel" },
            Effects = new[] { Flat(LongRangeRefund, 0.35f) },
        },
        new("piercing-shot", "PIERCING SHOT", LongGun.AbilityId, 35, "THE SHOT PASSES THROUGH ONE SHIP AND FLIES ON.")
        {
            Requires = new[] { "heavy-shot" },
            Effects = new[] { Flat(Pierce, 1) },
        },
        new("deadeye", "DEADEYE", LongGun.AbilityId, 60, "+75% DAMAGE AT LONG RANGE.")
        {
            Requires = new[] { "rangefinder" },
            Effects = new[] { Flat(LongRangeDamage, 0.75f) },
        },
        new("hullbreaker", "HULLBREAKER", LongGun.AbilityId, 60, "+35% DAMAGE. PASSES THROUGH ONE ADDITIONAL SHIP.")
        {
            Requires = new[] { "piercing-shot" },
            Effects = new[] { Percent(Damage, 0.35f), Flat(Pierce, 1) },
        },

        // Mortar: large area destruction or repeated bombardment.
        new("heavy-shell", "HEAVY SHELL", Mortar.AbilityId, 20, "+30% BLAST RADIUS.")
        {
            Excludes = new[] { "quick-fuse" },
            Effects = new[] { Percent(BlastRadius, 0.3f) },
        },
        new("quick-fuse", "QUICK FUSE", Mortar.AbilityId, 20, "SHELLS LAND 35% SOONER.")
        {
            Excludes = new[] { "heavy-shell" },
            Effects = new[] { Percent(FlightTime, -0.35f) },
        },
        new("cluster-shell", "CLUSTER SHELL", Mortar.AbilityId, 35, "EACH IMPACT SCATTERS 4 BLASTS, EACH AT 25% DAMAGE.")
        {
            Requires = new[] { "heavy-shell" },
            Effects = new[] { Flat(ClusterCount, 4) },
        },
        new("bombardment", "BOMBARDMENT", Mortar.AbilityId, 35, "FIRES 3 SHELLS IN SEQUENCE AT 50% DAMAGE EACH.")
        {
            Requires = new[] { "quick-fuse" },
            Effects = new[] { Flat(ShotCount, 2), Percent(Damage, -0.5f) },
        },
        new("earthshaker", "EARTHSHAKER", Mortar.AbilityId, 60,
            "+40% DAMAGE, +20% BLAST RADIUS AND +15% RELOAD TIME.")
        {
            Requires = new[] { "cluster-shell" },
            Effects = new[] { Percent(Damage, 0.4f), Percent(BlastRadius, 0.2f), Percent(Cooldown, 0.15f) },
        },
        new("rain-of-fire", "RAIN OF FIRE", Mortar.AbilityId, 60, "SALVO SHELLS DEAL 60% BASE DAMAGE. -25% RELOAD TIME.")
        {
            Requires = new[] { "bombardment" },
            Effects = new[] { Percent(Damage, 0.1f), Percent(Cooldown, -0.25f) },
        },
    };

    public static SkillDefinition? Find(string id) => All.FirstOrDefault(s => s.Id == id);

    /// <summary>One weapon's tree, in catalog order.</summary>
    public static IReadOnlyList<SkillDefinition> For(string abilityId) => All.Where(s => s.AbilityId == abilityId).ToList();

    /// <summary>1 for a skill with no prerequisites, otherwise one deeper than its deepest prerequisite.</summary>
    public static int TierOf(SkillDefinition skill) =>
        1 + skill.Prerequisites.Select(Find).OfType<SkillDefinition>().Select(TierOf).DefaultIfEmpty(0).Max();

    /// <summary>
    /// Whether owning <paramref name="owned"/> rules a skill out for good: it excludes (or is excluded by) one owned, or
    /// every route to it goes through skills that are ruled out.
    /// </summary>
    public static bool IsClosedOff(IReadOnlyCollection<string> owned, SkillDefinition skill)
    {
        if (owned.Contains(skill.Id))
            return false;
        if (owned.Select(Find).OfType<SkillDefinition>().Any(o => AreExclusive(o, skill)))
            return true;
        bool ClosedOff(string id) => Find(id) is not { } prerequisite || IsClosedOff(owned, prerequisite);
        return skill.Requires.Any(ClosedOff) || (skill.RequiresAny.Count > 0 && skill.RequiresAny.All(ClosedOff));
    }

    /// <summary>The skills in its tree that buying <paramref name="skill"/> would rule out, given what's <paramref name="owned"/> now.</summary>
    public static IReadOnlyList<SkillDefinition> WouldCloseOff(IReadOnlyCollection<string> owned, SkillDefinition skill)
    {
        var after = owned.Append(skill.Id).ToList();
        return For(skill.AbilityId)
            .Where(other => other.Id != skill.Id && !IsClosedOff(owned, other) && IsClosedOff(after, other))
            .ToList();
    }

    /// <summary>Skills that build directly on <paramref name="skill"/>.</summary>
    public static IReadOnlyList<SkillDefinition> LeadsTo(SkillDefinition skill) =>
        All.Where(other => other.Prerequisites.Contains(skill.Id)).ToList();

    /// <summary>Whether owning <paramref name="a"/> rules out <paramref name="b"/> (exclusion works both ways round).</summary>
    public static bool AreExclusive(SkillDefinition a, SkillDefinition b) => a.Excludes.Contains(b.Id) || b.Excludes.Contains(a.Id);
}
