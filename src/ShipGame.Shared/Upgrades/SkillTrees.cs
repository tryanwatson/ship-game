using ShipGame.Shared.Abilities;
using static ShipGame.Shared.Abilities.AbilityStat;
using static ShipGame.Shared.Upgrades.SkillEffect;

namespace ShipGame.Shared.Upgrades;

/// <summary>
/// Every weapon's skill tree. Each is two mutually exclusive branches of two skills, crowned by a capstone either
/// branch leads to, so a ship can own at most three of a tree's five. Add a skill by adding a row: its effects are
/// read by the ability (see <see cref="AbilityStat"/>), and the shipyard lays the tree out from the prerequisites.
/// Rows are listed tier by tier, left branch first.
/// </summary>
public static class SkillTrees
{
    public static readonly IReadOnlyList<SkillDefinition> All = new SkillDefinition[]
    {
        // ---- Broadside: a brawler's heavy weight of metal, or a skirmisher's quick, mobile fire ----------------
        new("heavy-volley", "HEAVY VOLLEY", BroadsideVolley.AbilityId, 20, "+2 CANNON PER VOLLEY. RELOADS 30% SLOWER.")
        {
            Excludes = new[] { "rapid-guns" },
            Effects = new[] { Flat(ShotCount, 2), Percent(Cooldown, 0.3f) },
        },
        new("rapid-guns", "RAPID GUNS", BroadsideVolley.AbilityId, 20, "1 FEWER CANNON PER VOLLEY. RELOADS 40% FASTER.")
        {
            Excludes = new[] { "heavy-volley" },
            Effects = new[] { Flat(ShotCount, -1), Percent(Cooldown, -0.4f) },
        },
        new("point-blank", "POINT BLANK", BroadsideVolley.AbilityId, 35, "+60% DAMAGE TO SHIPS WITHIN HALF RANGE.")
        {
            Requires = new[] { "heavy-volley" },
            Effects = new[] { Flat(CloseRangeDamage, 0.6f) },
        },
        new("running-guns", "RUNNING GUNS", BroadsideVolley.AbilityId, 35, "+40% DAMAGE WHEN FIRED NEAR FULL SPEED.")
        {
            Requires = new[] { "rapid-guns" },
            Effects = new[] { Flat(SpeedDamage, 0.4f) },
        },
        new("devastating-volley", "DEVASTATING VOLLEY", BroadsideVolley.AbilityId, 60,
            "+4 CANNON AND +25% DAMAGE. RELOADS 80% SLOWER.")
        {
            RequiresAny = new[] { "point-blank", "running-guns" },
            Effects = new[] { Flat(ShotCount, 4), Percent(Damage, 0.25f), Percent(Cooldown, 0.8f) },
        },

        // ---- Long gun: a reach-and-reload sniper, or a slow, heavy shot that punches through ------------------
        new("rifled-barrel", "RIFLED BARREL", LongGun.AbilityId, 20, "+40% SHOT SPEED AND +35% RANGE.")
        {
            Excludes = new[] { "heavy-shot" },
            Effects = new[] { Percent(ProjectileSpeed, 0.4f), Percent(AbilityStat.Range, 0.35f) },
        },
        new("heavy-shot", "HEAVY SHOT", LongGun.AbilityId, 20, "+60% DAMAGE. 15% SLOWER SHOT, RELOADS 40% SLOWER.")
        {
            Excludes = new[] { "rifled-barrel" },
            Effects = new[] { Percent(Damage, 0.6f), Percent(ProjectileSpeed, -0.15f), Percent(Cooldown, 0.4f) },
        },
        new("rangefinder", "RANGEFINDER", LongGun.AbilityId, 35, "LONG RANGE HITS REFUND HALF THE RELOAD.")
        {
            Requires = new[] { "rifled-barrel" },
            Effects = new[] { Flat(LongRangeRefund, 0.5f) },
        },
        new("piercing-shot", "PIERCING SHOT", LongGun.AbilityId, 35, "THE SHOT PASSES THROUGH ONE SHIP AND FLIES ON.")
        {
            Requires = new[] { "heavy-shot" },
            Effects = new[] { Flat(Pierce, 1) },
        },
        new("deadeye", "DEADEYE", LongGun.AbilityId, 60, "+80% DAMAGE AT LONG RANGE. RELOADS 25% SLOWER.")
        {
            RequiresAny = new[] { "rangefinder", "piercing-shot" },
            Effects = new[] { Flat(LongRangeDamage, 0.8f), Percent(Cooldown, 0.25f) },
        },

        // ---- Mortar: big lingering blasts, or fast and many shells ----------------------------------------
        new("heavy-shell", "HEAVY SHELL", Mortar.AbilityId, 20, "+40% BLAST RADIUS. RELOADS 20% SLOWER.")
        {
            Excludes = new[] { "quick-fuse" },
            Effects = new[] { Percent(BlastRadius, 0.4f), Percent(Cooldown, 0.2f) },
        },
        new("quick-fuse", "QUICK FUSE", Mortar.AbilityId, 20, "SHELLS LAND 45% SOONER. 15% SMALLER BLAST.")
        {
            Excludes = new[] { "heavy-shell" },
            Effects = new[] { Percent(FlightTime, -0.45f), Percent(BlastRadius, -0.15f) },
        },
        new("cluster-shell", "CLUSTER SHELL", Mortar.AbilityId, 35, "EACH IMPACT SCATTERS 4 SMALL BLASTS AROUND IT.")
        {
            Requires = new[] { "heavy-shell" },
            Effects = new[] { Flat(ClusterCount, 4) },
        },
        new("bombardment", "BOMBARDMENT", Mortar.AbilityId, 35, "FIRES 3 SHELLS IN SEQUENCE. -45% DAMAGE EACH.")
        {
            Requires = new[] { "quick-fuse" },
            Effects = new[] { Flat(ShotCount, 2), Percent(Damage, -0.45f) },
        },
        new("siege-artillery", "SIEGE ARTILLERY", Mortar.AbilityId, 60,
            "+60% RANGE, +30% BLAST AND +25% DAMAGE. RELOADS 60% SLOWER.")
        {
            RequiresAny = new[] { "cluster-shell", "bombardment" },
            Effects = new[] { Percent(AbilityStat.Range, 0.6f), Percent(BlastRadius, 0.3f), Percent(Damage, 0.25f), Percent(Cooldown, 0.6f) },
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
