using System;
using System.Collections.Generic;

[Serializable]
public class APConnectionState
{
    public string Host = "archipelago.gg";
    public int Port = 12345;
    public string SlotName = "Wastelander";
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

    public List<string> ReceivedAPItemKeys = new List<string>();
    public List<string> VanillaConsumedAPItemKeys = new List<string>();

    public APConnectionState Connection = new APConnectionState();

    public APWorldOptions Options = new APWorldOptions();

    public bool HeartglazeFlowerReceivedFromAP { get; set; } = false;
    public bool HeartglazeFlowerDeferredNoticeShown { get; set; } = false;

    public int LastProcessedReceivedItemIndex = 0;
    public bool GoalReported = false;

    public bool HarpoonPieceDeferredDeliveryNoticeShown = false;

    public List<long> SentLocationIds = new List<long>();

    public int SessionDeaths = 0;
    public int DeathsSinceLastDeathLink = 0;

    public List<string> APUnlockedMapAreaIds { get; set; } = new List<string>();

    public bool HarpoonPiece1ReceivedFromAP { get; set; } = false;
    public bool HarpoonPiece2ReceivedFromAP { get; set; } = false;
    public bool HarpoonPieceDeferredNoticeShown { get; set; } = false;
    public bool RadioSilenceDashBypassNoticeShown { get; set; } = false;

    public bool TutorialHookDebrisEventObserved { get; set; } = false;
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

public enum VisceraProtectionMode
{
    Off = 0,
    DeathLinkOnly = 1,
    AllDeaths = 2
}

public class APWorldOptions
{
    public WeaponGrantMode WeaponMode { get; set; } = WeaponGrantMode.Direct;
    public bool DeathLinkEnabled { get; set; } = false;
    public bool DeathAmnestyEnabled { get; set; } = false;
    public int DeathAmnestyCount { get; set; } = 1;

    public bool DeathLinkLocalOverrideEnabled = false;
    public bool DeathAmnestyLocalOverrideEnabled = false;
    public bool DeathAmnestyCountLocalOverrideEnabled = false;

    // Missing fields in older save files retain v0.1.5 behavior.
    public bool SkipJakobTransition { get; set; } = true;
    public bool SkipOrellaTransition { get; set; } = true;
    public bool SkipRoyBoat { get; set; } = true;

    // Seed-controlled presentation option.
    // False preserves the private-preview behavior used by older seeds.
    public bool AutomaticPurchaseHints { get; set; } = false;

    // Optional top-left victory checklist. Enabled by default for new and old seeds.
    public bool ShowGoalChecklist { get; set; } = true;

    // Configurable victory goals. C# defaults intentionally preserve the
    // pre-goal-options behavior for old seeds that do not send these fields.
    public List<string> GoalCategories { get; set; } = new List<string>
    {
        "bosses"
    };
    public int GoalAmount { get; set; } = 1;

    public List<string> BossGoals { get; set; } = new List<string>
    {
        "Two-Beak God"
    };
    public int BossGoalAmount { get; set; } = 1;
    public int PuppyGiftGoalAmount { get; set; } = 7;
    public int WastelanderGoalAmount { get; set; } = 6;

    public VisceraProtectionMode VisceraProtection { get; set; }
        = VisceraProtectionMode.Off;
}

public class APLocationPreview
{
    public long LocationId { get; private set; }
    public long ItemId { get; private set; }
    public string ItemName { get; private set; }
    public int RecipientSlot { get; private set; }
    public string RecipientName { get; private set; }

    public APLocationPreview(
        long locationId,
        long itemId,
        string itemName,
        int recipientSlot,
        string recipientName)
    {
        LocationId = locationId;
        ItemId = itemId;
        ItemName = itemName;
        RecipientSlot = recipientSlot;
        RecipientName = recipientName;
    }

    public string DisplayText
    {
        get
        {
            return ItemName + " for " + RecipientName;
        }
    }
}

public class PendingItem
{
    public ItemKind Kind { get; private set; }
    public string Id { get; private set; }
    public int Amount { get; private set; }
    public string DisplayName { get; private set; }
    public long ApItemId { get; private set; } = -1;
    public string SourcePlayerName { get; private set; }
    public string SourceLocationName { get; private set; }
    public bool PresentationQueued { get; private set; }

    public PendingItem(ItemKind kind, string id, int amount, string displayName)
    {
        Kind = kind;
        Id = id;
        Amount = amount;
        DisplayName = displayName;
    }

    public void SetApItemId(long apItemId)
    {
        ApItemId = apItemId;
    }

    public void SetReceiveMetadata(string playerName, string locationName)
    {
        SourcePlayerName = playerName;
        SourceLocationName = locationName;
    }

    public void MarkPresentationQueued()
    {
        PresentationQueued = true;
    }

    public void ResetPresentationQueued()
    {
        PresentationQueued = false;
    }

    public void AddAmount(int amount)
    {
        Amount += amount;
    }

    public override string ToString()
    {
        return $"Kind={Kind}, Id={Id}, Amount={Amount}, DisplayName={DisplayName}, ApItemId={ApItemId}, SourcePlayer={SourcePlayerName}";
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