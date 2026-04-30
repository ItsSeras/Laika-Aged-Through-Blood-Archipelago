using System;
using System.Collections.Generic;

[Serializable]
public class APConnectionState
{
    public string Host = "archipelago.gg";
    public int Port = 36979;
    public string SlotName = "Seras";
    public string Password = "";

    public bool IsConnected = false;
    public bool IsAuthenticated = false;

    public int Team = 0;
    public int Slot = 0;
}

[Serializable]
public class APSaveState
{
    public int SaveSlotIndex = 0;
    public bool APEnabled = false;

    public string SessionIdentityKey = "";
    public string SessionSeedName = "";

    public APConnectionState Connection = new APConnectionState();

    public APWorldOptions Options = new APWorldOptions();

    public bool HeartglazeFlowerReceivedFromAP { get; set; } = false;
    public bool HeartglazeFlowerDeferredNoticeShown { get; set; } = false;

    public int LastProcessedReceivedItemIndex = 0;
    public bool GoalReported = false;

    public List<long> SentLocationIds = new List<long>();

    public int SessionDeaths = 0;
    public int DeathsSinceLastDeathLink = 0;
}

public enum ItemKind
{
    Currency,
    Weapon,
    WeaponUpgrade,
    Ingredient,
    Material,
    Collectible,
    PuppyTreat,
    KeyItem,
    MapUnlock,
    Unknown
}

public enum WeaponGrantMode
{
    Direct,
    Crafting
}

public class APWorldOptions
{
    public WeaponGrantMode WeaponMode { get; set; } = WeaponGrantMode.Direct;
    public bool DeathLinkEnabled { get; set; } = false;
    public bool DeathAmnestyEnabled { get; set; } = false;
    public int DeathAmnestyCount { get; set; } = 1;
}

public class PendingItem
{
    public ItemKind Kind { get; private set; }
    public string Id { get; private set; }
    public int Amount { get; private set; }
    public string DisplayName { get; private set; }

    public PendingItem(ItemKind kind, string id, int amount, string displayName)
    {
        Kind = kind;
        Id = id;
        Amount = amount;
        DisplayName = displayName;
    }

    public void AddAmount(int amount)
    {
        Amount += amount;
    }

    public override string ToString()
    {
        return $"Kind={Kind}, Id={Id}, Amount={Amount}, DisplayName={DisplayName}";
    }
}

public class APLocationDefinition
{
    public long LocationId { get; private set; }
    public string DisplayName { get; private set; }
    public string InternalId { get; private set; }
    public string Category { get; private set; }
    public bool RecoverableFromSave { get; private set; }

    public APLocationDefinition(
        long locationId,
        string displayName,
        string internalId,
        string category,
        bool recoverableFromSave)
    {
        LocationId = locationId;
        DisplayName = displayName;
        InternalId = internalId;
        Category = category;
        RecoverableFromSave = recoverableFromSave;
    }

    public override string ToString()
    {
        return
            $"LocationId={LocationId}, " +
            $"DisplayName={DisplayName}, " +
            $"InternalId={InternalId}, " +
            $"Category={Category}, " +
            $"RecoverableFromSave={RecoverableFromSave}";
    }
}