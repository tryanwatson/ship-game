using System.Numerics;
using LiteNetLib.Utils;
using ShipGame.Shared.Abilities;
using ShipGame.Shared.Commands;
using ShipGame.Shared.Progression;
using ShipGame.Shared.Simulation;
using ShipGame.Shared.Stats;
using ShipGame.Shared.Trading;
using ShipGame.Shared.Upgrades;

namespace ShipGame.Net;

/// <summary>
/// Hand-written binary encoding for everything on the wire. Plain floats for now; quantization is a phase 3
/// bandwidth job. Every Write has a matching Read directly below it.
/// </summary>
public static class Wire
{
    // ---- Primitives -------------------------------------------------------------------------------------

    public static void Put(this NetDataWriter w, Vector2 v)
    {
        w.Put(v.X);
        w.Put(v.Y);
    }

    public static Vector2 GetVector2(this NetDataReader r) => new(r.GetFloat(), r.GetFloat());

    /// <summary>Ids are positive; -1 stands for "none".</summary>
    public static void PutOptional(this NetDataWriter w, int? value) => w.Put(value ?? -1);

    public static int? GetOptionalInt(this NetDataReader r)
    {
        var value = r.GetInt();
        return value < 0 ? null : value;
    }

    public static void Put(this NetDataWriter w, TradeContract c)
    {
        w.Put(c.Id); w.Put(c.OriginIslandId); w.Put(c.DestinationIslandId); w.Put(c.Cost); w.Put(c.Payout); w.Put(c.CargoUnits);
    }

    public static TradeContract GetContract(this NetDataReader r) =>
        new(r.GetInt(), r.GetInt(), r.GetInt(), r.GetInt(), r.GetInt(), r.GetInt());

    public static void Put(this NetDataWriter w, CargoLot lot)
    {
        w.Put(lot.Contract);
        w.Put(lot.RemainingUnits);
    }

    public static CargoLot GetCargoLot(this NetDataReader r) => new(r.GetContract(), r.GetInt());

    /// <summary>A card by id, with the level its numbers were dealt at and its roll.</summary>
    public static void Put(this NetDataWriter w, CardPick card)
    {
        w.Put(card.Id);
        w.Put((byte)Math.Clamp(card.Level, 0, byte.MaxValue));
        w.Put(card.Roll);
    }

    public static CardPick GetCardPick(this NetDataReader r) => new(r.GetString(64), r.GetByte(), r.GetFloat());

    public static void Put(this NetDataWriter w, CardOffer offer)
    {
        w.Put((byte)offer.Source);
        w.Put((byte)Math.Clamp(offer.Level, 0, byte.MaxValue));
        w.Put((byte)Math.Clamp(offer.FreeRerolls, 0, byte.MaxValue));
        w.Put((byte)offer.Cards.Count);
        foreach (var card in offer.Cards)
            w.Put(card);
    }

    public static CardOffer GetCardOffer(this NetDataReader r)
    {
        var source = (OfferSource)r.GetByte();
        var level = r.GetByte();
        var free = r.GetByte();
        var count = r.GetByte();
        var cards = new CardPick[count];
        for (var i = 0; i < count; i++)
            cards[i] = r.GetCardPick();
        return new CardOffer(source, level, cards, free);
    }

    // ---- Commands (client -> server; PlayerId is never sent, the server knows who's talking) ----------

    private enum CommandTag : byte
    {
        Move = 1,
        Stop = 2,
        AdjustThrottle = 3,
        SetRudder = 4,
        CastAbility = 5,
        AnchorKey = 6,
        ChoosePlunder = 7,
        PurchaseUpgrade = 8,
        PurchaseContract = 9,
        UnlockAbility = 10,
        PurchaseSkill = 11,
        PurchaseRepair = 12,
        ChooseCard = 13,
        RerollCards = 14,
        ChooseStartingWeapon = 15,
    }

    public static void PutCommand(this NetDataWriter w, Command command)
    {
        switch (command)
        {
            case MoveCommand move:
                w.Put((byte)CommandTag.Move);
                w.Put(move.Target);
                break;
            case StopCommand:
                w.Put((byte)CommandTag.Stop);
                break;
            case AdjustThrottleCommand adjust:
                w.Put((byte)CommandTag.AdjustThrottle);
                w.Put((sbyte)Math.Clamp(adjust.Delta, sbyte.MinValue, sbyte.MaxValue));
                break;
            case SetRudderCommand rudder:
                w.Put((byte)CommandTag.SetRudder);
                w.Put((sbyte)Math.Clamp(rudder.Rudder, -1, 1));
                break;
            case CastAbilityCommand cast:
                w.Put((byte)CommandTag.CastAbility);
                w.Put((byte)cast.Slot);
                w.Put(cast.Target);
                w.Put(cast.ViewTick ?? -1L);
                break;
            case AnchorKeyCommand anchor:
                w.Put((byte)CommandTag.AnchorKey);
                w.Put(anchor.Pressed);
                break;
            case ChoosePlunderCommand:
                w.Put((byte)CommandTag.ChoosePlunder);
                break;
            case PurchaseUpgradeCommand purchase:
                w.Put((byte)CommandTag.PurchaseUpgrade);
                w.Put(purchase.UpgradeId);
                break;
            case PurchaseContractCommand contract:
                w.Put((byte)CommandTag.PurchaseContract);
                w.Put(contract.ContractId);
                break;
            case UnlockAbilityCommand unlock:
                w.Put((byte)CommandTag.UnlockAbility);
                w.Put(unlock.AbilityId);
                break;
            case PurchaseRepairCommand:
                w.Put((byte)CommandTag.PurchaseRepair);
                break;
            case PurchaseSkillCommand skill:
                w.Put((byte)CommandTag.PurchaseSkill);
                w.Put(skill.SkillId);
                break;
            case ChooseCardCommand card:
                w.Put((byte)CommandTag.ChooseCard);
                w.Put(card.CardId);
                break;
            case RerollCardsCommand:
                w.Put((byte)CommandTag.RerollCards);
                break;
            case ChooseStartingWeaponCommand weapon:
                w.Put((byte)CommandTag.ChooseStartingWeapon);
                w.Put(weapon.AbilityId);
                break;
            default:
                throw new ArgumentException($"No wire format for {command.GetType().Name}.");
        }
    }

    /// <summary>Reads a command on behalf of <paramref name="playerId"/>: whatever the client claims, it only commands itself.</summary>
    public static Command GetCommand(this NetDataReader r, int playerId) => (CommandTag)r.GetByte() switch
    {
        CommandTag.Move => new MoveCommand(playerId, r.GetVector2()),
        CommandTag.Stop => new StopCommand(playerId),
        CommandTag.AdjustThrottle => new AdjustThrottleCommand(playerId, r.GetSByte()),
        CommandTag.SetRudder => new SetRudderCommand(playerId, r.GetSByte()),
        CommandTag.CastAbility => new CastAbilityCommand(playerId, (AbilitySlot)r.GetByte(), r.GetVector2(), r.GetLong() is >= 0 and var seen ? seen : null),
        CommandTag.AnchorKey => new AnchorKeyCommand(playerId, r.GetBool()),
        CommandTag.ChoosePlunder => new ChoosePlunderCommand(playerId),
        CommandTag.PurchaseUpgrade => new PurchaseUpgradeCommand(playerId, r.GetString(64)),
        CommandTag.PurchaseContract => new PurchaseContractCommand(playerId, r.GetInt()),
        CommandTag.UnlockAbility => new UnlockAbilityCommand(playerId, r.GetString(64)),
        CommandTag.PurchaseSkill => new PurchaseSkillCommand(playerId, r.GetString(64)),
        CommandTag.PurchaseRepair => new PurchaseRepairCommand(playerId),
        CommandTag.ChooseCard => new ChooseCardCommand(playerId, r.GetString(64)),
        CommandTag.RerollCards => new RerollCardsCommand(playerId),
        CommandTag.ChooseStartingWeapon => new ChooseStartingWeaponCommand(playerId, r.GetString(64)),
        var tag => throw new InvalidDataException($"Unknown command tag {tag}."),
    };

    // ---- World events (server -> client) ----------------------------------------------------------------

    private enum EventTag : byte
    {
        ShipSpawned = 1,
        ShipSunk = 2,
        ProjectileSpawned = 3,
        ProjectileImpact = 4,
        AbilityCast = 5,
        ShipGrounded = 6,
        GoldChanged = 7,
        IslandPlundered = 8,
        UpgradePurchased = 9,
        CommandRejected = 11,
        PlayerSunk = 12,
        PlayerRespawned = 13,
        RunEnded = 14,
        AreaStrikeLaunched = 15,
        AreaStrikeImpact = 16,
        AreaDiscovered = 17,
        ContractsOffered = 18,
        ContractPurchased = 19,
        ContractDelivered = 20,
        CargoDropped = 21,
        CargoRecovered = 22,
        CargoLost = 23,
        AbilityUnlocked = 24,
        SkillPurchased = 25,
        ShipHidden = 26,
        FortressTaken = 27,
        CardsOffered = 28,
        CardChosen = 29,
        BossSpawned = 30,
        ShotWarned = 31,
        CardsRerolled = 32,
        StartingWeaponChosen = 33,
        ShipRammed = 34,
        FireStarted = 35,
    }

    public static void PutEvent(this NetDataWriter w, WorldEvent e)
    {
        switch (e)
        {
            case ShipSpawned x:
                Begin(w, EventTag.ShipSpawned, x); w.Put(x.ShipId);
                break;
            case ShipSunk x:
                Begin(w, EventTag.ShipSunk, x); w.Put(x.ShipId); w.PutOptional(x.KillerShipId);
                break;
            case ProjectileSpawned x:
                Begin(w, EventTag.ProjectileSpawned, x);
                w.Put(x.ProjectileId); w.Put(x.OwnerShipId); w.Put((byte)x.Team);
                w.Put(x.Position); w.Put(x.Velocity); w.Put(x.Damage); w.Put(x.LifetimeTicks); w.Put(x.Radius);
                break;
            case AreaStrikeLaunched x:
                Begin(w, EventTag.AreaStrikeLaunched, x);
                w.Put(x.StrikeId); w.Put(x.OwnerShipId); w.Put((byte)x.Team);
                w.Put(x.Origin); w.Put(x.Target); w.Put(x.Radius); w.Put(x.Damage); w.Put(x.ImpactTick);
                break;
            case AreaStrikeImpact x:
                Begin(w, EventTag.AreaStrikeImpact, x); w.Put(x.StrikeId); w.Put(x.Target); w.Put(x.Radius);
                break;
            case ShotWarned x:
                Begin(w, EventTag.ShotWarned, x); w.Put(x.ShipId); w.Put((byte)x.Slot); w.Put(x.Target); w.Put(x.FireTick); w.Put((byte)x.Channel);
                break;
            case AreaDiscovered x:
                Begin(w, EventTag.AreaDiscovered, x);
                w.Put((byte)x.Team);
                w.Put((ushort)x.Cells.Count);
                foreach (var cell in x.Cells)
                    w.Put((ushort)cell);
                break;
            case ProjectileImpact x:
                Begin(w, EventTag.ProjectileImpact, x); w.Put(x.ProjectileId); w.PutOptional(x.ShipId); w.Put(x.PassedThrough);
                break;
            case AbilityCast x:
                Begin(w, EventTag.AbilityCast, x); w.Put(x.ShipId); w.Put((byte)x.Slot); w.Put(x.CooldownTicks); w.Put((byte)x.Channel);
                break;
            case ShipGrounded x:
                Begin(w, EventTag.ShipGrounded, x); w.Put(x.ShipId);
                break;
            case GoldChanged x:
                Begin(w, EventTag.GoldChanged, x); w.Put(x.PlayerId); w.Put(x.Gold); w.Put(x.Delta);
                break;
            case IslandPlundered x:
                Begin(w, EventTag.IslandPlundered, x); w.Put(x.IslandId); w.Put(x.PlayerId); w.Put(x.Gold);
                break;
            case UpgradePurchased x:
                Begin(w, EventTag.UpgradePurchased, x); w.Put(x.ShipId); w.Put(x.UpgradeId); w.Put(x.Level);
                break;
            case AbilityUnlocked x:
                Begin(w, EventTag.AbilityUnlocked, x); w.Put(x.ShipId); w.Put(x.AbilityId); w.Put((byte)x.Slot);
                break;
            case SkillPurchased x:
                Begin(w, EventTag.SkillPurchased, x); w.Put(x.ShipId); w.Put(x.SkillId);
                break;
            case ShipHidden x:
                Begin(w, EventTag.ShipHidden, x); w.Put(x.ShipId);
                break;
            case CommandRejected x:
                Begin(w, EventTag.CommandRejected, x); w.Put(x.PlayerId); w.PutCommand(x.Command); w.Put((byte)x.Reason);
                break;
            case PlayerSunk x:
                Begin(w, EventTag.PlayerSunk, x); w.Put(x.PlayerId); w.Put(x.RespawnTicks);
                break;
            case PlayerRespawned x:
                Begin(w, EventTag.PlayerRespawned, x); w.Put(x.PlayerId); w.Put(x.ShipId);
                break;
            case RunEnded x:
                Begin(w, EventTag.RunEnded, x); w.Put(x.Victory);
                break;
            case ContractsOffered x:
                Begin(w, EventTag.ContractsOffered, x);
                w.Put(x.IslandId);
                w.Put((byte)x.Offers.Count);
                foreach (var contract in x.Offers)
                    w.Put(contract);
                break;
            case ContractPurchased x:
                Begin(w, EventTag.ContractPurchased, x); w.Put(x.ShipId); w.Put(x.PlayerId); w.Put(x.Contract);
                break;
            case ContractDelivered x:
                Begin(w, EventTag.ContractDelivered, x); w.Put(x.ShipId); w.Put(x.PlayerId); w.Put(x.ContractId); w.Put(x.Payout);
                break;
            case CargoDropped x:
                Begin(w, EventTag.CargoDropped, x); w.Put(x.CrateId); w.Put(x.Position); w.Put(x.Cargo);
                break;
            case CargoRecovered x:
                Begin(w, EventTag.CargoRecovered, x); w.Put(x.CrateId); w.Put(x.ShipId); w.Put(x.PlayerId);
                break;
            case CargoLost x:
                Begin(w, EventTag.CargoLost, x); w.Put(x.ContractId);
                break;
            case FortressTaken x:
                Begin(w, EventTag.FortressTaken, x); w.Put(x.IslandId);
                break;
            case CardsOffered x:
                Begin(w, EventTag.CardsOffered, x); w.Put(x.PlayerId); w.Put(x.Offer);
                break;
            case CardChosen x:
                Begin(w, EventTag.CardChosen, x); w.Put(x.PlayerId); w.Put(x.Card);
                break;
            case ShipRammed x:
                Begin(w, EventTag.ShipRammed, x); w.Put(x.RammerShipId); w.Put(x.TargetShipId);
                break;
            case FireStarted x:
                Begin(w, EventTag.FireStarted, x);
                w.Put(x.FireId); w.Put(x.OwnerShipId); w.Put((byte)x.Team); w.Put(x.Position); w.Put(x.Radius); w.Put(x.Dps); w.Put(x.EndTick);
                break;
            case StartingWeaponChosen x:
                Begin(w, EventTag.StartingWeaponChosen, x); w.Put(x.PlayerId); w.Put(x.AbilityId);
                break;
            case CardsRerolled x:
                Begin(w, EventTag.CardsRerolled, x);
                w.Put(x.PlayerId);
                w.Put(x.Offer);
                w.Put((ushort)Math.Clamp(x.Rerolls, 0, ushort.MaxValue));
                break;
            case BossSpawned x:
                Begin(w, EventTag.BossSpawned, x); w.Put(x.ShipId); w.Put((byte)x.Round); w.Put(x.PreyPlayerId);
                break;
            default:
                throw new ArgumentException($"No wire format for {e.GetType().Name}.");
        }
    }

    private static void Begin(NetDataWriter w, EventTag tag, WorldEvent e)
    {
        w.Put((byte)tag);
        w.Put(e.Tick);
    }

    public static WorldEvent GetEvent(this NetDataReader r)
    {
        var tag = (EventTag)r.GetByte();
        var tick = r.GetLong();
        switch (tag)
        {
            case EventTag.ShipSpawned: return new ShipSpawned(tick, r.GetInt());
            case EventTag.ShipSunk: return new ShipSunk(tick, r.GetInt(), r.GetOptionalInt());
            case EventTag.ProjectileSpawned:
                return new ProjectileSpawned(tick, r.GetInt(), r.GetInt(), (Team)r.GetByte(), r.GetVector2(), r.GetVector2(), r.GetFloat(), r.GetInt(), r.GetFloat());
            case EventTag.AreaStrikeLaunched:
                return new AreaStrikeLaunched(tick, r.GetInt(), r.GetInt(), (Team)r.GetByte(), r.GetVector2(), r.GetVector2(), r.GetFloat(), r.GetFloat(), r.GetLong());
            case EventTag.AreaStrikeImpact:
                return new AreaStrikeImpact(tick, r.GetInt(), r.GetVector2(), r.GetFloat());
            case EventTag.ShotWarned:
                return new ShotWarned(tick, r.GetInt(), (AbilitySlot)r.GetByte(), r.GetVector2(), r.GetLong(), r.GetByte());
            case EventTag.AreaDiscovered:
            {
                var team = (Team)r.GetByte();
                var count = r.GetUShort();
                var cells = new int[count];
                for (var i = 0; i < count; i++)
                    cells[i] = r.GetUShort();
                return new AreaDiscovered(tick, team, cells);
            }
            case EventTag.ProjectileImpact: return new ProjectileImpact(tick, r.GetInt(), r.GetOptionalInt(), r.GetBool());
            case EventTag.AbilityCast: return new AbilityCast(tick, r.GetInt(), (AbilitySlot)r.GetByte(), r.GetInt(), r.GetByte());
            case EventTag.ShipGrounded: return new ShipGrounded(tick, r.GetInt());
            case EventTag.GoldChanged: return new GoldChanged(tick, r.GetInt(), r.GetInt(), r.GetInt());
            case EventTag.IslandPlundered: return new IslandPlundered(tick, r.GetInt(), r.GetInt(), r.GetInt());
            case EventTag.UpgradePurchased: return new UpgradePurchased(tick, r.GetInt(), r.GetString(64), r.GetInt());
            case EventTag.AbilityUnlocked: return new AbilityUnlocked(tick, r.GetInt(), r.GetString(64), (AbilitySlot)r.GetByte());
            case EventTag.SkillPurchased: return new SkillPurchased(tick, r.GetInt(), r.GetString(64));
            case EventTag.ShipHidden: return new ShipHidden(tick, r.GetInt());
            case EventTag.CommandRejected:
            {
                var playerId = r.GetInt();
                var command = r.GetCommand(playerId);
                return new CommandRejected(tick, playerId, command, (RejectionReason)r.GetByte());
            }
            case EventTag.PlayerSunk: return new PlayerSunk(tick, r.GetInt(), r.GetInt());
            case EventTag.PlayerRespawned: return new PlayerRespawned(tick, r.GetInt(), r.GetInt());
            case EventTag.RunEnded: return new RunEnded(tick, r.GetBool());
            case EventTag.ContractsOffered:
            {
                var islandId = r.GetInt();
                var count = r.GetByte();
                var offers = new TradeContract[count];
                for (var i = 0; i < count; i++)
                    offers[i] = r.GetContract();
                return new ContractsOffered(tick, islandId, offers);
            }
            case EventTag.ContractPurchased: return new ContractPurchased(tick, r.GetInt(), r.GetInt(), r.GetContract());
            case EventTag.ContractDelivered: return new ContractDelivered(tick, r.GetInt(), r.GetInt(), r.GetInt(), r.GetInt());
            case EventTag.CargoDropped: return new CargoDropped(tick, r.GetInt(), r.GetVector2(), r.GetCargoLot());
            case EventTag.CargoRecovered: return new CargoRecovered(tick, r.GetInt(), r.GetInt(), r.GetInt());
            case EventTag.CargoLost: return new CargoLost(tick, r.GetInt());
            case EventTag.FortressTaken: return new FortressTaken(tick, r.GetInt());
            case EventTag.CardsOffered: return new CardsOffered(tick, r.GetInt(), r.GetCardOffer());
            case EventTag.CardChosen: return new CardChosen(tick, r.GetInt(), r.GetCardPick());
            case EventTag.ShipRammed: return new ShipRammed(tick, r.GetInt(), r.GetInt());
            case EventTag.FireStarted:
                return new FireStarted(tick, r.GetInt(), r.GetInt(), (Team)r.GetByte(), r.GetVector2(), r.GetFloat(), r.GetFloat(), r.GetLong());
            case EventTag.StartingWeaponChosen: return new StartingWeaponChosen(tick, r.GetInt(), r.GetString(64));
            case EventTag.CardsRerolled: return new CardsRerolled(tick, r.GetInt(), r.GetCardOffer(), r.GetUShort());
            case EventTag.BossSpawned: return new BossSpawned(tick, r.GetInt(), r.GetByte(), r.GetInt());
            default: throw new InvalidDataException($"Unknown event tag {tag}.");
        }
    }

    // ---- Lobby / run start ------------------------------------------------------------------------------

    public static void PutLobby(this NetDataWriter w, LobbyState lobby)
    {
        w.Put(lobby.RunInProgress);
        w.Put(lobby.FriendlyFire);
        w.Put(lobby.StartingGold);
        w.Put((byte)lobby.Players.Count);
        foreach (var player in lobby.Players)
        {
            w.Put(player.PlayerId);
            w.Put(player.Ready);
            w.Put(player.Name);
        }
    }

    public static LobbyState GetLobby(this NetDataReader r)
    {
        var running = r.GetBool();
        var friendlyFire = r.GetBool();
        var startingGold = r.GetInt();
        var count = r.GetByte();
        var players = new List<LobbyPlayer>(count);
        for (var i = 0; i < count; i++)
        {
            var id = r.GetInt();
            var ready = r.GetBool();
            players.Add(new LobbyPlayer(id, ready, r.GetString(64)));
        }
        return new LobbyState(running, players, friendlyFire, startingGold);
    }

    public static void PutRunStart(this NetDataWriter w, RunStart start)
    {
        w.Put(start.Tick);
        w.Put(start.WorldSize);
        w.Put(start.Wind);
        w.Put(start.FriendlyFire);
        var crew = start.Crew ?? Array.Empty<(int, string)>();
        w.Put((byte)crew.Count);
        foreach (var (playerId, name) in crew)
        {
            w.Put(playerId);
            w.Put(name);
        }
    }

    public static RunStart GetRunStart(this NetDataReader r)
    {
        var tick = r.GetLong();
        var size = r.GetVector2();
        var wind = r.GetVector2();
        var friendlyFire = r.GetBool();
        var count = r.GetByte();
        var crew = new List<(int, string)>(count);
        for (var i = 0; i < count; i++)
            crew.Add((r.GetInt(), r.GetString(64)));
        return new RunStart(tick, size, wind, friendlyFire, crew);
    }

    // ---- Ship info --------------------------------------------------------------------------------------

    public static void PutShipInfo(this NetDataWriter w, ShipInfo info)
    {
        w.Put(info.Tick);
        w.Put(info.ShipId);
        w.PutOptional(info.OwnerPlayerId);
        w.Put((byte)info.Team);
        PutStats(w, info.BaseStats);
        w.Put((byte)info.AbilityIds.Count);
        foreach (var id in info.AbilityIds)
            w.Put(id ?? "");
        w.Put((ushort)info.Modifiers.Count);
        foreach (var m in info.Modifiers)
        {
            w.Put((byte)m.Stat);
            w.Put((byte)m.Kind);
            w.Put(m.Value);
            w.Put(m.Source);
        }
        w.Put(info.Position);
        w.Put(info.Heading);
        var skills = info.SkillIds ?? Array.Empty<string>();
        w.Put((byte)skills.Count);
        foreach (var id in skills)
            w.Put(id);
        w.Put((byte)Math.Clamp(info.Level, 0, byte.MaxValue));
        w.Put(info.IsBoss);
        w.PutOptional(info.FortIslandId);
        var cards = info.Cards ?? Array.Empty<CardPick>();
        w.Put((byte)cards.Count);
        foreach (var card in cards)
            w.Put(card);
    }

    public static ShipInfo GetShipInfo(this NetDataReader r)
    {
        var tick = r.GetLong();
        var shipId = r.GetInt();
        var owner = r.GetOptionalInt();
        var team = (Team)r.GetByte();
        var stats = GetStats(r);
        var abilityCount = r.GetByte();
        var abilities = new List<string?>(abilityCount);
        for (var i = 0; i < abilityCount; i++)
        {
            var id = r.GetString(64);
            abilities.Add(id.Length == 0 ? null : id);
        }
        var modifierCount = r.GetUShort();
        var modifiers = new List<StatModifier>(modifierCount);
        for (var i = 0; i < modifierCount; i++)
            modifiers.Add(new StatModifier((StatId)r.GetByte(), (ModifierKind)r.GetByte(), r.GetFloat(), r.GetString(64)));
        var position = r.GetVector2();
        var heading = r.GetFloat();
        var skillCount = r.GetByte();
        var skills = new List<string>(skillCount);
        for (var i = 0; i < skillCount; i++)
            skills.Add(r.GetString(64));
        var level = r.GetByte();
        var isBoss = r.GetBool();
        var fortIslandId = r.GetOptionalInt();
        var cardCount = r.GetByte();
        var cards = new List<CardPick>(cardCount);
        for (var i = 0; i < cardCount; i++)
            cards.Add(r.GetCardPick());
        return new ShipInfo(tick, shipId, owner, team, stats, abilities, modifiers, position, heading, skills, level, isBoss,
            fortIslandId, cards);
    }

    private static void PutStats(NetDataWriter w, ShipStats s)
    {
        foreach (var value in new[]
                 {
                     s.MaxSpeed, s.Acceleration, s.CoastTimeConstant, s.MinDeceleration, s.MinTurnRadius, s.TurnRadiusAtMaxSpeed,
                     s.Radius, s.Length, s.Beam, s.MaxHealth, s.CooldownSpeed, s.WeaponDamage, s.ProjectileSpeed, s.WeaponRange, s.CargoCapacity, s.HealthRegen,
                 })
            w.Put(value);
    }

    private static ShipStats GetStats(NetDataReader r) => new(
        MaxSpeed: r.GetFloat(), Acceleration: r.GetFloat(), CoastTimeConstant: r.GetFloat(), MinDeceleration: r.GetFloat(),
        MinTurnRadius: r.GetFloat(), TurnRadiusAtMaxSpeed: r.GetFloat(), Radius: r.GetFloat(), Length: r.GetFloat(),
        Beam: r.GetFloat(), MaxHealth: r.GetFloat(), CooldownSpeed: r.GetFloat(), WeaponDamage: r.GetFloat(),
        ProjectileSpeed: r.GetFloat(), WeaponRange: r.GetFloat(), CargoCapacity: r.GetFloat(), HealthRegen: r.GetFloat());

    // ---- Snapshots (chunked: each chunk is one unreliable packet) ---------------------------------------

    /// <summary>
    /// Splits a snapshot into packets of at most <paramref name="maxBytes"/>, as many ships to a packet as fit. The
    /// first chunk also carries the world-level header (players, command acks, run progress, island cooldowns).
    /// </summary>
    public static List<NetDataWriter> WriteSnapshotChunks(Snapshot snapshot, int maxBytes = Protocol.MaxSnapshotChunkBytes)
    {
        var header = new NetDataWriter();
        header.Put(snapshot.Wind);
        var run = snapshot.Run;
        header.Put((byte)Math.Clamp(run.FortressesTaken, 0, byte.MaxValue));
        header.Put((byte)Math.Clamp(run.BossesSunk, 0, byte.MaxValue));
        header.Put((ushort)Math.Clamp(run.BossCountdownTicks, 0, ushort.MaxValue));
        header.Put(run.BossAfloat);
        header.Put(snapshot.Paused);
        header.Put(snapshot.RunOver);
        header.Put(snapshot.Victory);
        header.Put((byte)snapshot.Players.Count);
        foreach (var p in snapshot.Players)
        {
            header.Put(p.PlayerId); header.Put(p.Gold); header.Put(p.Kills); header.Put(p.RespawnTicks);
        }
        header.Put((byte)snapshot.CommandAcks.Count);
        foreach (var (playerId, sequence) in snapshot.CommandAcks)
        {
            header.Put(playerId); header.Put(sequence);
        }
        header.Put((byte)snapshot.PlunderedIslands.Count);
        foreach (var islandId in snapshot.PlunderedIslands)
            header.Put(islandId);

        // Pack ships greedily: type + tick + sequence + index + count + ship count, then the header in the first chunk.
        const int fixedBytes = 1 + 8 + 4 + 1 + 1 + 1;
        var shipBytes = snapshot.Ships.Select(ship =>
        {
            var w = new NetDataWriter();
            PutShipState(w, ship);
            return w;
        }).ToList();
        var groups = new List<List<NetDataWriter>> { new() };
        var used = fixedBytes + header.Length;
        if (used > maxBytes)
            throw new InvalidOperationException($"Snapshot header of {header.Length} bytes doesn't fit a {maxBytes}-byte packet.");
        foreach (var ship in shipBytes)
        {
            if (used + ship.Length > maxBytes || groups[^1].Count == byte.MaxValue)
            {
                groups.Add(new List<NetDataWriter>());
                used = fixedBytes;
            }
            groups[^1].Add(ship);
            used += ship.Length;
        }

        var chunks = new List<NetDataWriter>(groups.Count);
        for (var chunk = 0; chunk < groups.Count; chunk++)
        {
            var w = new NetDataWriter();
            w.Put((byte)MessageType.SnapshotChunk);
            w.Put(snapshot.Tick);
            w.Put(snapshot.Sequence);
            w.Put((byte)chunk);
            w.Put((byte)groups.Count);
            if (chunk == 0)
                w.Put(header.Data, 0, header.Length);
            w.Put((byte)groups[chunk].Count);
            foreach (var ship in groups[chunk])
                w.Put(ship.Data, 0, ship.Length);
            chunks.Add(w);
        }
        return chunks;
    }

    /// <summary>One chunk as read off the wire (after its <see cref="MessageType"/> byte).</summary>
    public sealed record SnapshotChunk(long Tick, uint Sequence, int Index, int Count, Snapshot Partial);

    public static SnapshotChunk GetSnapshotChunk(this NetDataReader r)
    {
        var tick = r.GetLong();
        var sequence = r.GetUInt();
        var index = r.GetByte();
        var count = r.GetByte();
        var snapshot = new Snapshot { Tick = tick, Sequence = sequence };
        if (index == 0)
        {
            snapshot.Wind = r.GetVector2();
            snapshot.Run = new RunStatus(FortressesTaken: r.GetByte(), BossesSunk: r.GetByte(), BossCountdownTicks: r.GetUShort(),
                BossAfloat: r.GetBool());
            snapshot.Paused = r.GetBool();
            snapshot.RunOver = r.GetBool();
            snapshot.Victory = r.GetBool();
            var players = r.GetByte();
            for (var i = 0; i < players; i++)
                snapshot.Players.Add(new PlayerSnapshot(r.GetInt(), r.GetInt(), r.GetInt(), r.GetInt()));
            var acks = r.GetByte();
            for (var i = 0; i < acks; i++)
                snapshot.CommandAcks.Add((r.GetInt(), r.GetUInt()));
            var islands = r.GetByte();
            for (var i = 0; i < islands; i++)
                snapshot.PlunderedIslands.Add(r.GetInt());
        }

        var ships = r.GetByte();
        for (var i = 0; i < ships; i++)
            snapshot.Ships.Add(GetShipState(r));
        return new SnapshotChunk(tick, sequence, index, count, snapshot);
    }

    private static void PutShipState(NetDataWriter w, ShipState s)
    {
        w.Put(s.ShipId);
        w.Put(s.Position);
        w.Put(s.Heading);
        w.Put(s.Speed);
        w.Put(s.Health);
        w.Put(s.Throttle);
        w.Put(s.Rudder);
        w.Put((byte)s.Anchor);
        w.Put((ushort)Math.Clamp(s.AnchorRaiseTicks, 0, ushort.MaxValue));
        w.Put((byte)Math.Clamp(s.AnchorDropTicks, 0, byte.MaxValue));
        w.PutOptional(s.PlunderIslandId);
        w.Put((ushort)Math.Clamp(s.PlunderTicks, 0, ushort.MaxValue));
        w.Put((byte)s.Stance);
        w.Put(s.MoveTarget.HasValue);
        if (s.MoveTarget is { } target)
            w.Put(target);
        w.Put(s.IsHoldingCourse);
        w.Put(s.WindDrift);
        w.Put(s.Marked);
        foreach (var channels in s.Cooldowns)
        {
            var count = channels?.Length ?? 0;
            w.Put((byte)count);
            for (var c = 0; c < count; c++)
            {
                w.Put((ushort)Math.Clamp(channels![c].Remaining, 0, ushort.MaxValue));
                w.Put((ushort)Math.Clamp(channels[c].Duration, 0, ushort.MaxValue));
            }
        }
    }

    private static ShipState GetShipState(NetDataReader r)
    {
        var s = new ShipState
        {
            ShipId = r.GetInt(),
            Position = r.GetVector2(),
            Heading = r.GetFloat(),
            Speed = r.GetFloat(),
            Health = r.GetFloat(),
            Throttle = r.GetSByte(),
            Rudder = r.GetSByte(),
            Anchor = (AnchorState)r.GetByte(),
            AnchorRaiseTicks = r.GetUShort(),
            AnchorDropTicks = r.GetByte(),
            PlunderIslandId = r.GetOptionalInt(),
            PlunderTicks = r.GetUShort(),
            Stance = (NpcStance)r.GetByte(),
        };
        if (r.GetBool())
            s.MoveTarget = r.GetVector2();
        s.IsHoldingCourse = r.GetBool();
        s.WindDrift = r.GetVector2();
        s.Marked = r.GetBool();
        for (var i = 0; i < s.Cooldowns.Length; i++)
        {
            var count = r.GetByte();
            var channels = new (int, int)[count];
            for (var c = 0; c < count; c++)
                channels[c] = (r.GetUShort(), r.GetUShort());
            s.Cooldowns[i] = channels;
        }
        return s;
    }
}
