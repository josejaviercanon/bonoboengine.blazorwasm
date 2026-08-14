# G65 — Economy & Shop Systems

> **Category:** Guide · **Related:** [G10 Custom Game Systems §1 Inventory](./G10_custom_game_systems.md) · [G5 UI Framework](./G5_ui_framework.md) · [G64 Combat & Damage Systems](./G64_combat_damage_systems.md) · [G37 Tilemap Systems](./G37_tilemap_systems.md) · [G4 AI Systems](./G4_ai_systems.md)

> Complete implementation guide for currency, shops, transactions, and economic balancing in MonoGame + Arch ECS. Covers both tower-defense economies (earn-and-spend loops) and survival economies (gather-craft-trade loops). All systems are composable — use the pieces your genre needs.

---

## Table of Contents

1. [Design Philosophy](#1--design-philosophy)
2. [Currency Components](#2--currency-components)
3. [Wallet & Currency Manager](#3--wallet--currency-manager)
4. [Transaction Pipeline](#4--transaction-pipeline)
5. [Item Pricing & Price Modifiers](#5--item-pricing--price-modifiers)
6. [Shop System](#6--shop-system)
7. [Tower Defense Economy](#7--tower-defense-economy)
8. [Survival & Trading Economy](#8--survival--trading-economy)
9. [Loot & Drop Tables](#9--loot--drop-tables)
10. [Economy Sinks & Faucets](#10--economy-sinks--faucets)
11. [Save/Load Integration](#11--saveload-integration)
12. [UI Integration](#12--ui-integration)
13. [Economy Tuning Reference](#13--economy-tuning-reference)

---

## 1 — Design Philosophy

### Why a Dedicated Economy System?

Most games shove currency into a single `int gold` field and call it done. This works for a jam game but falls apart when you need:

- **Multiple currencies** (gold, gems, souls, wood, stone)
- **Dynamic pricing** (supply/demand, reputation discounts, inflation)
- **Transaction validation** (can't buy if you can't afford it — sounds obvious until it's a bug)
- **Economy balancing** (too much money = no tension; too little = frustration)

### Core Principles

1. **All transactions go through one pipeline.** Never modify currency directly — always use `TransactionProcessor`. This gives you a single point for logging, validation, events, and anti-cheat.
2. **Currencies are data, not code.** Define them in JSON/registry so designers can add new ones without recompiling.
3. **Separate earning from spending.** Faucets (income sources) and sinks (spending opportunities) are tracked independently for balance analysis.
4. **Events over callbacks.** Every transaction fires an ECS event so UI, audio, achievements, and analytics can react without coupling.

---

## 2 — Currency Components

### Currency Definition

```csharp
/// <summary>
/// Data definition for a currency type. Lives in a registry, not ECS.
/// </summary>
public record CurrencyDef(
    string Id,           // "gold", "gems", "wood", "tower_credits"
    string DisplayName,  // "Gold Coins"
    string Icon,         // Sprite/texture key for UI
    int MaxAmount,       // Cap (int.MaxValue for uncapped)
    bool Persistent,     // Survives between runs? (meta-currency)
    bool ShowInHUD       // Always visible in HUD?
);

/// <summary>
/// Categories help UI group currencies and apply rules.
/// </summary>
public enum CurrencyCategory
{
    Primary,    // Gold, coins — main spending currency
    Premium,    // Gems, crystals — rare/paid currency
    Resource,   // Wood, stone, iron — crafting materials
    Meta        // XP, reputation — progression currencies
}
```

### Currency Registry

```csharp
public class CurrencyRegistry
{
    private readonly Dictionary<string, CurrencyDef> _currencies = new();

    public void Register(CurrencyDef def) => _currencies[def.Id] = def;
    public CurrencyDef Get(string id) => _currencies[id];
    public bool Exists(string id) => _currencies.ContainsKey(id);
    public IEnumerable<CurrencyDef> All => _currencies.Values;

    public void LoadFromJson(string json)
    {
        var defs = JsonSerializer.Deserialize<List<CurrencyDef>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        foreach (var def in defs!) Register(def);
    }
}
```

### Example Currency Data (JSON)

```json
[
  {
    "Id": "gold",
    "DisplayName": "Gold",
    "Icon": "icon_gold",
    "MaxAmount": 999999,
    "Persistent": false,
    "ShowInHUD": true
  },
  {
    "Id": "gems",
    "DisplayName": "Gems",
    "Icon": "icon_gem",
    "MaxAmount": 9999,
    "Persistent": true,
    "ShowInHUD": true
  },
  {
    "Id": "wood",
    "DisplayName": "Wood",
    "Icon": "icon_wood",
    "MaxAmount": 9999,
    "Persistent": false,
    "ShowInHUD": false
  }
]
```

### ECS Components

```csharp
/// <summary>
/// Holds all currency balances for an entity (player, NPC merchant, faction).
/// </summary>
public record struct Wallet(Dictionary<string, int> Balances)
{
    public Wallet() : this(new Dictionary<string, int>()) { }

    public readonly int Get(string currencyId) =>
        Balances.TryGetValue(currencyId, out var amount) ? amount : 0;

    public readonly bool Has(string currencyId, int amount) =>
        Get(currencyId) >= amount;
}

/// <summary>
/// Marks an entity as a shop/vendor. Attach alongside Wallet.
/// </summary>
public record struct Shop(
    string ShopId,           // Lookup key for shop inventory
    float BuyMultiplier,     // Price multiplier when player buys (1.0 = base price)
    float SellMultiplier,    // Price multiplier when player sells (0.5 = 50% of base)
    bool Restocks,           // Does inventory replenish?
    float RestockTimerSec    // Seconds between restocks
);

/// <summary>Event: fired after any successful transaction.</summary>
public record struct TransactionEvent(
    string CurrencyId,
    int Amount,
    TransactionType Type,
    string Source     // "enemy_kill", "shop_purchase", "wave_bonus", etc.
);

public enum TransactionType { Earn, Spend, Transfer }
```

---

## 3 — Wallet & Currency Manager

The `WalletOps` static class handles all direct wallet mutations. Nothing else should touch `Wallet.Balances` directly.

```csharp
public static class WalletOps
{
    /// <summary>Add currency, clamped to max. Returns actual amount added.</summary>
    public static int Add(ref Wallet wallet, string currencyId, int amount, CurrencyRegistry registry)
    {
        var def = registry.Get(currencyId);
        int current = wallet.Get(currencyId);
        int space = def.MaxAmount - current;
        int actual = Math.Min(amount, space);

        if (actual <= 0) return 0;

        wallet.Balances[currencyId] = current + actual;
        return actual;
    }

    /// <summary>Remove currency. Returns false if insufficient funds (no partial removal).</summary>
    public static bool Remove(ref Wallet wallet, string currencyId, int amount)
    {
        int current = wallet.Get(currencyId);
        if (current < amount) return false;

        wallet.Balances[currencyId] = current - amount;
        return true;
    }

    /// <summary>Transfer currency between two wallets. Atomic — fails if source can't cover it.</summary>
    public static bool Transfer(
        ref Wallet source, ref Wallet target,
        string currencyId, int amount, CurrencyRegistry registry)
    {
        if (!source.Has(currencyId, amount)) return false;

        Remove(ref source, currencyId, amount);
        Add(ref target, currencyId, amount, registry);
        return true;
    }

    /// <summary>Set currency to exact value (for save/load or debug).</summary>
    public static void Set(ref Wallet wallet, string currencyId, int amount, CurrencyRegistry registry)
    {
        var def = registry.Get(currencyId);
        wallet.Balances[currencyId] = Math.Clamp(amount, 0, def.MaxAmount);
    }

    /// <summary>Check if wallet can afford a list of costs.</summary>
    public static bool CanAfford(in Wallet wallet, IReadOnlyList<CurrencyCost> costs)
    {
        foreach (var cost in costs)
        {
            if (wallet.Get(cost.CurrencyId) < cost.Amount) return false;
        }
        return true;
    }
}

public record struct CurrencyCost(string CurrencyId, int Amount);
```

---

## 4 — Transaction Pipeline

Every currency change flows through `TransactionProcessor`. This is the enforcement layer — validation, events, logging.

```csharp
public class TransactionProcessor
{
    private readonly CurrencyRegistry _registry;
    private readonly World _world;
    private readonly List<ITransactionModifier> _modifiers = new();

    // Analytics tracking
    private readonly Dictionary<string, long> _totalEarned = new();
    private readonly Dictionary<string, long> _totalSpent = new();

    public TransactionProcessor(CurrencyRegistry registry, World world)
    {
        _registry = registry;
        _world = world;
    }

    /// <summary>Register a modifier (discounts, taxes, bonuses).</summary>
    public void AddModifier(ITransactionModifier mod) => _modifiers.Add(mod);
    public void RemoveModifier(ITransactionModifier mod) => _modifiers.Remove(mod);

    /// <summary>Process an earning transaction (faucet).</summary>
    public TransactionResult Earn(ref Wallet wallet, string currencyId, int baseAmount, string source)
    {
        int amount = baseAmount;
        foreach (var mod in _modifiers)
            amount = mod.ModifyEarning(currencyId, amount, source);

        if (amount <= 0)
            return new TransactionResult(false, 0, "Amount reduced to zero by modifiers");

        int actual = WalletOps.Add(ref wallet, currencyId, amount, _registry);

        // Track
        _totalEarned.TryGetValue(currencyId, out long prev);
        _totalEarned[currencyId] = prev + actual;

        // Fire event
        _world.Create(new TransactionEvent(currencyId, actual, TransactionType.Earn, source));

        return new TransactionResult(true, actual,
            actual < amount ? "Wallet full — partial deposit" : null);
    }

    /// <summary>Process a spending transaction (sink).</summary>
    public TransactionResult Spend(ref Wallet wallet, string currencyId, int baseAmount, string source)
    {
        int amount = baseAmount;
        foreach (var mod in _modifiers)
            amount = mod.ModifySpending(currencyId, amount, source);

        if (!WalletOps.Remove(ref wallet, currencyId, amount))
            return new TransactionResult(false, 0, "Insufficient funds");

        _totalSpent.TryGetValue(currencyId, out long prev);
        _totalSpent[currencyId] = prev + amount;

        _world.Create(new TransactionEvent(currencyId, amount, TransactionType.Spend, source));

        return new TransactionResult(true, amount, null);
    }

    /// <summary>Multi-currency purchase (all-or-nothing).</summary>
    public TransactionResult SpendMulti(ref Wallet wallet, IReadOnlyList<CurrencyCost> costs, string source)
    {
        // Validate all costs first
        if (!WalletOps.CanAfford(in wallet, costs))
            return new TransactionResult(false, 0, "Cannot afford one or more costs");

        // Apply all
        foreach (var cost in costs)
            Spend(ref wallet, cost.CurrencyId, cost.Amount, source);

        return new TransactionResult(true, costs.Sum(c => c.Amount), null);
    }

    /// <summary>Get analytics snapshot for balancing.</summary>
    public EconomySnapshot GetSnapshot() => new(
        new Dictionary<string, long>(_totalEarned),
        new Dictionary<string, long>(_totalSpent)
    );
}

public record TransactionResult(bool Success, int ActualAmount, string? Message);

public record EconomySnapshot(
    Dictionary<string, long> TotalEarned,
    Dictionary<string, long> TotalSpent
)
{
    public long NetFlow(string currencyId) =>
        TotalEarned.GetValueOrDefault(currencyId) - TotalSpent.GetValueOrDefault(currencyId);
}
```

### Transaction Modifiers

```csharp
/// <summary>
/// Modifiers alter transaction amounts. Stack them for discounts, taxes, bonuses, etc.
/// </summary>
public interface ITransactionModifier
{
    int ModifyEarning(string currencyId, int amount, string source) => amount;
    int ModifySpending(string currencyId, int amount, string source) => amount;
}

/// <summary>Flat percentage bonus to all earnings of a currency type.</summary>
public class EarningBonus : ITransactionModifier
{
    public string CurrencyId { get; init; }
    public float Multiplier { get; init; } // 1.5 = +50%

    public int ModifyEarning(string currencyId, int amount, string source) =>
        currencyId == CurrencyId ? (int)(amount * Multiplier) : amount;

    public int ModifySpending(string currencyId, int amount, string source) => amount;
}

/// <summary>Discount on purchases from a specific source (e.g., reputation discount).</summary>
public class ShopDiscount : ITransactionModifier
{
    public string ShopId { get; init; } = "";
    public float DiscountPercent { get; init; } // 0.1 = 10% off

    public int ModifyEarning(string currencyId, int amount, string source) => amount;

    public int ModifySpending(string currencyId, int amount, string source) =>
        source.StartsWith("shop:")
            ? Math.Max(1, (int)(amount * (1f - DiscountPercent)))
            : amount;
}
```

---

## 5 — Item Pricing & Price Modifiers

### Price Table

Decouple item prices from item definitions. This lets the same item cost different amounts at different shops or times.

```csharp
/// <summary>
/// Base price for an item in a given currency.
/// </summary>
public record struct ItemPrice(
    string ItemId,
    string CurrencyId,
    int BasePrice
);

/// <summary>
/// Central price lookup with modifier support.
/// </summary>
public class PriceTable
{
    private readonly Dictionary<string, ItemPrice> _prices = new();
    private readonly List<IPriceModifier> _modifiers = new();

    public void SetPrice(string itemId, string currencyId, int basePrice)
    {
        _prices[itemId] = new ItemPrice(itemId, currencyId, basePrice);
    }

    public void AddModifier(IPriceModifier mod) => _modifiers.Add(mod);

    /// <summary>Get the final buy price (what player pays).</summary>
    public PriceResult GetBuyPrice(string itemId, string shopId)
    {
        if (!_prices.TryGetValue(itemId, out var price))
            return new PriceResult(false, "", 0, 0);

        int final = price.BasePrice;
        foreach (var mod in _modifiers)
            final = mod.ModifyPrice(itemId, final, shopId, isBuying: true);

        return new PriceResult(true, price.CurrencyId, price.BasePrice, Math.Max(1, final));
    }

    /// <summary>Get the sell price (what player receives). Typically 30-50% of buy price.</summary>
    public PriceResult GetSellPrice(string itemId, string shopId, float sellRatio = 0.4f)
    {
        if (!_prices.TryGetValue(itemId, out var price))
            return new PriceResult(false, "", 0, 0);

        int baseSell = Math.Max(1, (int)(price.BasePrice * sellRatio));
        int final = baseSell;
        foreach (var mod in _modifiers)
            final = mod.ModifyPrice(itemId, final, shopId, isBuying: false);

        return new PriceResult(true, price.CurrencyId, baseSell, Math.Max(1, final));
    }

    public void LoadFromJson(string json)
    {
        var entries = JsonSerializer.Deserialize<List<ItemPrice>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        foreach (var e in entries!)
            _prices[e.ItemId] = e;
    }
}

public record PriceResult(bool Found, string CurrencyId, int BasePrice, int FinalPrice);

public interface IPriceModifier
{
    int ModifyPrice(string itemId, int currentPrice, string shopId, bool isBuying);
}
```

### Dynamic Pricing Examples

```csharp
/// <summary>
/// Supply/demand: price increases as the shop has fewer of an item.
/// Great for survival games where resources fluctuate.
/// </summary>
public class SupplyDemandModifier : IPriceModifier
{
    private readonly Dictionary<string, int> _shopStock = new();

    public void SetStock(string itemId, int count) => _shopStock[itemId] = count;

    public int ModifyPrice(string itemId, int currentPrice, string shopId, bool isBuying)
    {
        if (!_shopStock.TryGetValue(itemId, out int stock)) return currentPrice;

        // Low stock = higher price, high stock = lower price
        float modifier = stock switch
        {
            <= 0 => 2.0f,   // Out of stock — double price if restocking
            <= 3 => 1.5f,   // Scarce
            <= 10 => 1.0f,  // Normal
            <= 30 => 0.85f, // Surplus
            _ => 0.7f       // Abundant
        };

        return isBuying
            ? (int)(currentPrice * modifier)
            : (int)(currentPrice / modifier); // Inverse for selling
    }
}

/// <summary>
/// Time-of-day pricing. Night merchants charge more. Morning markets are cheaper.
/// </summary>
public class TimeOfDayModifier : IPriceModifier
{
    private readonly Func<float> _getTimeOfDay; // 0-24

    public TimeOfDayModifier(Func<float> getTimeOfDay) => _getTimeOfDay = getTimeOfDay;

    public int ModifyPrice(string itemId, int currentPrice, string shopId, bool isBuying)
    {
        float hour = _getTimeOfDay();
        float modifier = hour switch
        {
            >= 6 and < 12 => 0.9f,   // Morning market: 10% off
            >= 22 or < 6 => 1.3f,    // Night: 30% markup
            _ => 1.0f
        };
        return (int)(currentPrice * modifier);
    }
}
```

---

## 6 — Shop System

### Shop Inventory

```csharp
/// <summary>
/// Defines what a shop sells. Loaded from data.
/// </summary>
public record ShopListing(
    string ItemId,
    int Stock,          // -1 for unlimited
    int MaxStock,       // For restock cap
    bool Unlocked       // Some items only appear after quest/progression
);

public record ShopDef(
    string ShopId,
    string DisplayName,
    string CurrencyId,          // Primary currency this shop uses
    float BuyMultiplier,        // 1.0 = base price, 1.2 = 20% markup
    float SellMultiplier,       // 0.4 = player gets 40% of base price
    float RestockIntervalSec,   // 0 = no restock
    List<ShopListing> Listings
);

public class ShopRegistry
{
    private readonly Dictionary<string, ShopDef> _shops = new();

    public void Register(ShopDef def) => _shops[def.ShopId] = def;
    public ShopDef Get(string shopId) => _shops[shopId];
    public bool Exists(string shopId) => _shops.ContainsKey(shopId);

    public void LoadFromJson(string json)
    {
        var defs = JsonSerializer.Deserialize<List<ShopDef>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        foreach (var def in defs!) Register(def);
    }
}
```

### Shop Transaction Logic

```csharp
public class ShopProcessor
{
    private readonly ShopRegistry _shopRegistry;
    private readonly PriceTable _priceTable;
    private readonly TransactionProcessor _transactions;
    private readonly CurrencyRegistry _currencyRegistry;

    // Runtime shop state (stock levels, restock timers)
    private readonly Dictionary<string, Dictionary<string, int>> _liveStock = new();
    private readonly Dictionary<string, float> _restockTimers = new();

    public ShopProcessor(
        ShopRegistry shopRegistry,
        PriceTable priceTable,
        TransactionProcessor transactions,
        CurrencyRegistry currencyRegistry)
    {
        _shopRegistry = shopRegistry;
        _priceTable = priceTable;
        _transactions = transactions;
        _currencyRegistry = currencyRegistry;
    }

    /// <summary>Initialize shop runtime state from definition.</summary>
    public void InitShop(string shopId)
    {
        var def = _shopRegistry.Get(shopId);
        var stock = new Dictionary<string, int>();
        foreach (var listing in def.Listings)
        {
            stock[listing.ItemId] = listing.Stock;
        }
        _liveStock[shopId] = stock;
        _restockTimers[shopId] = def.RestockIntervalSec;
    }

    /// <summary>Player buys an item from a shop.</summary>
    public ShopResult Buy(ref Wallet playerWallet, string shopId, string itemId, int quantity = 1)
    {
        var def = _shopRegistry.Get(shopId);
        var stock = _liveStock[shopId];

        // Check stock
        if (stock.TryGetValue(itemId, out int available) && available != -1)
        {
            if (available < quantity)
                return new ShopResult(false, 0, $"Only {available} in stock");
        }

        // Calculate price
        var price = _priceTable.GetBuyPrice(itemId, shopId);
        if (!price.Found)
            return new ShopResult(false, 0, "Item not priced");

        int totalCost = (int)(price.FinalPrice * def.BuyMultiplier * quantity);

        // Process payment
        var txn = _transactions.Spend(ref playerWallet, def.CurrencyId, totalCost, $"shop:{shopId}");
        if (!txn.Success)
            return new ShopResult(false, 0, txn.Message ?? "Cannot afford");

        // Reduce stock
        if (available != -1)
            stock[itemId] = available - quantity;

        return new ShopResult(true, totalCost, null);
    }

    /// <summary>Player sells an item to a shop.</summary>
    public ShopResult Sell(ref Wallet playerWallet, string shopId, string itemId, int quantity = 1)
    {
        var def = _shopRegistry.Get(shopId);

        var price = _priceTable.GetSellPrice(itemId, shopId, def.SellMultiplier);
        if (!price.Found)
            return new ShopResult(false, 0, "Shop doesn't buy this item");

        int totalEarned = price.FinalPrice * quantity;

        var txn = _transactions.Earn(ref playerWallet, def.CurrencyId, totalEarned, $"sell:{shopId}");

        // Add to shop stock
        if (_liveStock.TryGetValue(shopId, out var stock))
        {
            stock.TryGetValue(itemId, out int current);
            if (current != -1)
                stock[itemId] = current + quantity;
        }

        return new ShopResult(txn.Success, totalEarned, txn.Message);
    }

    /// <summary>Call every frame. Handles restock timers.</summary>
    public void Update(float deltaTime)
    {
        foreach (var (shopId, _) in _restockTimers)
        {
            var def = _shopRegistry.Get(shopId);
            if (def.RestockIntervalSec <= 0) continue;

            _restockTimers[shopId] -= deltaTime;
            if (_restockTimers[shopId] <= 0)
            {
                RestockShop(shopId);
                _restockTimers[shopId] = def.RestockIntervalSec;
            }
        }
    }

    private void RestockShop(string shopId)
    {
        var def = _shopRegistry.Get(shopId);
        var stock = _liveStock[shopId];

        foreach (var listing in def.Listings)
        {
            if (listing.Stock == -1) continue; // Unlimited, skip
            stock.TryGetValue(listing.ItemId, out int current);
            stock[listing.ItemId] = Math.Min(current + listing.Stock / 4, listing.MaxStock);
        }
    }

    /// <summary>Get displayable shop listings with current prices and stock.</summary>
    public List<ShopDisplayItem> GetDisplayItems(string shopId, in Wallet playerWallet)
    {
        var def = _shopRegistry.Get(shopId);
        var stock = _liveStock[shopId];
        var result = new List<ShopDisplayItem>();

        foreach (var listing in def.Listings)
        {
            if (!listing.Unlocked) continue;

            var price = _priceTable.GetBuyPrice(listing.ItemId, shopId);
            int finalCost = (int)(price.FinalPrice * def.BuyMultiplier);
            bool canAfford = playerWallet.Has(def.CurrencyId, finalCost);
            int currentStock = stock.GetValueOrDefault(listing.ItemId, listing.Stock);

            result.Add(new ShopDisplayItem(
                listing.ItemId,
                finalCost,
                price.BasePrice,
                def.CurrencyId,
                currentStock,
                canAfford && currentStock != 0
            ));
        }

        return result;
    }
}

public record ShopResult(bool Success, int Amount, string? Message);

public record ShopDisplayItem(
    string ItemId,
    int FinalPrice,
    int BasePrice,
    string CurrencyId,
    int Stock,        // -1 = unlimited
    bool CanBuy
);
```

### Example Shop Data (JSON)

```json
{
  "ShopId": "village_general",
  "DisplayName": "General Store",
  "CurrencyId": "gold",
  "BuyMultiplier": 1.0,
  "SellMultiplier": 0.4,
  "RestockIntervalSec": 300,
  "Listings": [
    { "ItemId": "health_potion", "Stock": 10, "MaxStock": 10, "Unlocked": true },
    { "ItemId": "iron_sword", "Stock": 3, "MaxStock": 3, "Unlocked": true },
    { "ItemId": "fire_scroll", "Stock": 1, "MaxStock": 2, "Unlocked": false }
  ]
}
```

---

## 7 — Tower Defense Economy

Tower defense games have a tight earn-spend loop: enemies die → earn currency → buy/upgrade towers. The entire game pacing depends on economic balance.

### TD-Specific Components

```csharp
/// <summary>
/// Attached to the player/controller entity in a tower defense game.
/// Tracks wave economy state.
/// </summary>
public record struct TowerEconomy(
    int StartingGold,
    int InterestRatePercent,  // % bonus at wave end (e.g., 5 = 5%)
    int InterestCap,          // Max interest earned per wave
    int LivesRemaining
);

/// <summary>
/// Attached to enemy entities. Defines what they drop on death.
/// </summary>
public record struct EnemyBounty(
    string CurrencyId,
    int BaseAmount,
    float WaveScaling   // Multiplied by wave number for scaling
);

/// <summary>
/// Tower placement cost and upgrade costs.
/// </summary>
public record TowerCostDef(
    string TowerId,
    string CurrencyId,
    int PlacementCost,
    int[] UpgradeCosts,   // Index = upgrade level (0→1, 1→2, etc.)
    float SellRefundRatio // 0.6 = get back 60% of total spent
);
```

### Tower Economy System

```csharp
public class TowerEconomySystem
{
    private readonly TransactionProcessor _transactions;
    private readonly CurrencyRegistry _currencyRegistry;
    private readonly Dictionary<string, TowerCostDef> _towerCosts = new();

    // Track total investment per tower entity for sell refunds
    private readonly Dictionary<Entity, int> _towerInvestment = new();

    public TowerEconomySystem(TransactionProcessor transactions, CurrencyRegistry currencyRegistry)
    {
        _transactions = transactions;
        _currencyRegistry = currencyRegistry;
    }

    public void RegisterTower(TowerCostDef def) => _towerCosts[def.TowerId] = def;

    /// <summary>Called when an enemy dies. Awards bounty to player.</summary>
    public void OnEnemyKilled(ref Wallet playerWallet, in EnemyBounty bounty, int currentWave)
    {
        int amount = (int)(bounty.BaseAmount + bounty.BaseAmount * bounty.WaveScaling * (currentWave - 1));
        _transactions.Earn(ref playerWallet, bounty.CurrencyId, amount, $"enemy_kill_wave{currentWave}");
    }

    /// <summary>Called between waves. Awards interest on current gold.</summary>
    public void OnWaveEnd(ref Wallet playerWallet, in TowerEconomy economy)
    {
        int currentGold = playerWallet.Get("gold");
        int interest = Math.Min(
            currentGold * economy.InterestRatePercent / 100,
            economy.InterestCap
        );

        if (interest > 0)
            _transactions.Earn(ref playerWallet, "gold", interest, "wave_interest");
    }

    /// <summary>Attempt to place a tower. Returns false if can't afford.</summary>
    public bool TryPlaceTower(ref Wallet playerWallet, string towerId, Entity towerEntity)
    {
        if (!_towerCosts.TryGetValue(towerId, out var costs)) return false;

        var txn = _transactions.Spend(ref playerWallet, costs.CurrencyId,
            costs.PlacementCost, $"tower_place:{towerId}");

        if (!txn.Success) return false;

        _towerInvestment[towerEntity] = costs.PlacementCost;
        return true;
    }

    /// <summary>Attempt to upgrade a tower. Returns false if can't afford or max level.</summary>
    public bool TryUpgradeTower(ref Wallet playerWallet, string towerId, int currentLevel, Entity towerEntity)
    {
        if (!_towerCosts.TryGetValue(towerId, out var costs)) return false;
        if (currentLevel >= costs.UpgradeCosts.Length) return false; // Max level

        int upgradeCost = costs.UpgradeCosts[currentLevel];
        var txn = _transactions.Spend(ref playerWallet, costs.CurrencyId,
            upgradeCost, $"tower_upgrade:{towerId}_lv{currentLevel + 1}");

        if (!txn.Success) return false;

        _towerInvestment.TryGetValue(towerEntity, out int prev);
        _towerInvestment[towerEntity] = prev + upgradeCost;
        return true;
    }

    /// <summary>Sell a tower. Refunds a percentage of total investment.</summary>
    public int SellTower(ref Wallet playerWallet, string towerId, Entity towerEntity)
    {
        if (!_towerCosts.TryGetValue(towerId, out var costs)) return 0;
        if (!_towerInvestment.TryGetValue(towerEntity, out int invested)) return 0;

        int refund = (int)(invested * costs.SellRefundRatio);
        _transactions.Earn(ref playerWallet, costs.CurrencyId, refund, $"tower_sell:{towerId}");
        _towerInvestment.Remove(towerEntity);

        return refund;
    }

    /// <summary>Check if the player can afford a specific tower or upgrade.</summary>
    public AffordResult CheckAffordable(in Wallet wallet, string towerId, int? upgradeLevel = null)
    {
        if (!_towerCosts.TryGetValue(towerId, out var costs))
            return new AffordResult(false, 0, 0);

        int cost = upgradeLevel.HasValue && upgradeLevel.Value < costs.UpgradeCosts.Length
            ? costs.UpgradeCosts[upgradeLevel.Value]
            : costs.PlacementCost;

        int balance = wallet.Get(costs.CurrencyId);
        return new AffordResult(balance >= cost, cost, balance);
    }
}

public record AffordResult(bool CanAfford, int Cost, int CurrentBalance);
```

### Tower Cost Data (JSON)

```json
[
  {
    "TowerId": "arrow_tower",
    "CurrencyId": "gold",
    "PlacementCost": 100,
    "UpgradeCosts": [150, 250, 400],
    "SellRefundRatio": 0.6
  },
  {
    "TowerId": "cannon_tower",
    "CurrencyId": "gold",
    "PlacementCost": 200,
    "UpgradeCosts": [300, 500],
    "SellRefundRatio": 0.5
  },
  {
    "TowerId": "frost_tower",
    "CurrencyId": "gold",
    "PlacementCost": 150,
    "UpgradeCosts": [200, 350, 600],
    "SellRefundRatio": 0.6
  }
]
```

---

## 8 — Survival & Trading Economy

Survival economies are resource-driven. Players gather materials, craft items, and optionally trade with NPCs. Currency may be barter-based (no single "gold" — trade wood for iron) or use a standard coin.

### Barter System

```csharp
/// <summary>
/// A barter trade: exchange a set of items/resources for another set.
/// No currency involved — direct item-for-item.
/// </summary>
public record TradeDef(
    string TradeId,
    string Description,         // "3 Wood + 1 Iron → Iron Axe"
    List<CurrencyCost> Costs,   // What player gives (can be resources)
    List<CurrencyCost> Rewards, // What player receives
    int MaxTrades,              // -1 = unlimited
    bool RequiresReputation     // Unlocked by reputation level?
);

public class BarterSystem
{
    private readonly TransactionProcessor _transactions;
    private readonly CurrencyRegistry _currencyRegistry;
    private readonly Dictionary<string, TradeDef> _trades = new();
    private readonly Dictionary<string, int> _tradeCount = new();

    public BarterSystem(TransactionProcessor transactions, CurrencyRegistry currencyRegistry)
    {
        _transactions = transactions;
        _currencyRegistry = currencyRegistry;
    }

    public void RegisterTrade(TradeDef trade) => _trades[trade.TradeId] = trade;

    /// <summary>Execute a barter trade.</summary>
    public ShopResult ExecuteTrade(ref Wallet playerWallet, string tradeId)
    {
        if (!_trades.TryGetValue(tradeId, out var trade))
            return new ShopResult(false, 0, "Trade not found");

        // Check uses
        _tradeCount.TryGetValue(tradeId, out int used);
        if (trade.MaxTrades != -1 && used >= trade.MaxTrades)
            return new ShopResult(false, 0, "Trade exhausted");

        // Check all costs
        if (!WalletOps.CanAfford(in playerWallet, trade.Costs))
            return new ShopResult(false, 0, "Insufficient resources");

        // Deduct costs
        foreach (var cost in trade.Costs)
            _transactions.Spend(ref playerWallet, cost.CurrencyId, cost.Amount, $"barter:{tradeId}");

        // Grant rewards
        foreach (var reward in trade.Rewards)
            _transactions.Earn(ref playerWallet, reward.CurrencyId, reward.Amount, $"barter:{tradeId}");

        _tradeCount[tradeId] = used + 1;
        return new ShopResult(true, 0, null);
    }

    /// <summary>Get all available trades with affordability.</summary>
    public List<TradeDisplay> GetAvailableTrades(in Wallet playerWallet)
    {
        var result = new List<TradeDisplay>();
        foreach (var (id, trade) in _trades)
        {
            _tradeCount.TryGetValue(id, out int used);
            bool available = trade.MaxTrades == -1 || used < trade.MaxTrades;
            bool canAfford = WalletOps.CanAfford(in playerWallet, trade.Costs);

            result.Add(new TradeDisplay(id, trade.Description, trade.Costs, trade.Rewards,
                available, canAfford));
        }
        return result;
    }
}

public record TradeDisplay(
    string TradeId,
    string Description,
    List<CurrencyCost> Costs,
    List<CurrencyCost> Rewards,
    bool Available,
    bool CanAfford
);
```

### Reputation & Unlock System

```csharp
/// <summary>
/// Reputation with factions/NPCs affects prices and unlocks.
/// Common in survival games with trading posts.
/// </summary>
public record struct Reputation(Dictionary<string, int> Levels)
{
    public Reputation() : this(new Dictionary<string, int>()) { }

    public readonly int GetLevel(string factionId) =>
        Levels.TryGetValue(factionId, out var lv) ? lv : 0;
}

/// <summary>
/// Price modifier based on reputation. Higher rep = better prices.
/// </summary>
public class ReputationPriceModifier : IPriceModifier
{
    private readonly Func<string, int> _getRepLevel; // shopId → rep level

    public ReputationPriceModifier(Func<string, int> getRepLevel)
    {
        _getRepLevel = getRepLevel;
    }

    public int ModifyPrice(string itemId, int currentPrice, string shopId, bool isBuying)
    {
        int rep = _getRepLevel(shopId);

        // Each rep level = 3% better prices, up to 30%
        float discount = Math.Min(rep * 0.03f, 0.30f);

        return isBuying
            ? (int)(currentPrice * (1f - discount))   // Buying: pay less
            : (int)(currentPrice * (1f + discount));   // Selling: earn more
    }
}

/// <summary>
/// Earn reputation through trading volume.
/// </summary>
public class ReputationTracker
{
    // Track spending per shop → award rep when thresholds are crossed
    private readonly Dictionary<string, int> _spendingPerShop = new();
    private readonly int[] _repThresholds = { 500, 1500, 3000, 6000, 12000 };

    /// <summary>Call after every shop transaction.</summary>
    public int? TrackTransaction(string shopId, int amount, ref Reputation rep)
    {
        _spendingPerShop.TryGetValue(shopId, out int total);
        total += amount;
        _spendingPerShop[shopId] = total;

        int currentLevel = rep.GetLevel(shopId);
        if (currentLevel < _repThresholds.Length && total >= _repThresholds[currentLevel])
        {
            rep.Levels[shopId] = currentLevel + 1;
            return currentLevel + 1; // Return new level for UI notification
        }

        return null;
    }
}
```

---

## 9 — Loot & Drop Tables

Weighted random loot generation for enemies, chests, and resource nodes.

```csharp
/// <summary>
/// A single entry in a drop table.
/// </summary>
public record DropEntry(
    string CurrencyId,   // What drops (can be a resource "wood" or currency "gold")
    int MinAmount,
    int MaxAmount,
    float Weight,        // Relative weight for selection
    float DropChance     // 0-1, independent chance this drops at all
);

/// <summary>
/// A drop table defines possible drops from a source.
/// </summary>
public record DropTable(
    string TableId,
    List<DropEntry> Entries,
    int GuaranteedDrops,  // Always roll at least this many
    int MaxDrops          // Roll up to this many
);

public class LootGenerator
{
    private readonly Random _rng;
    private readonly Dictionary<string, DropTable> _tables = new();

    public LootGenerator(int? seed = null)
    {
        _rng = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    public void RegisterTable(DropTable table) => _tables[table.TableId] = table;

    /// <summary>Roll a drop table and return results.</summary>
    public List<CurrencyCost> Roll(string tableId, float luckMultiplier = 1.0f)
    {
        if (!_tables.TryGetValue(tableId, out var table))
            return new List<CurrencyCost>();

        var results = new List<CurrencyCost>();
        float totalWeight = table.Entries.Sum(e => e.Weight);

        int drops = 0;
        foreach (var entry in table.Entries)
        {
            if (drops >= table.MaxDrops) break;

            // Check individual drop chance
            float adjustedChance = Math.Min(entry.DropChance * luckMultiplier, 1.0f);
            if (_rng.NextDouble() > adjustedChance && drops >= table.GuaranteedDrops)
                continue;

            int amount = _rng.Next(entry.MinAmount, entry.MaxAmount + 1);
            if (amount > 0)
            {
                results.Add(new CurrencyCost(entry.CurrencyId, amount));
                drops++;
            }
        }

        // If we haven't hit guaranteed drops, force-roll weighted random
        while (drops < table.GuaranteedDrops && drops < table.MaxDrops)
        {
            var entry = WeightedPick(table.Entries, totalWeight);
            int amount = _rng.Next(entry.MinAmount, entry.MaxAmount + 1);
            results.Add(new CurrencyCost(entry.CurrencyId, Math.Max(1, amount)));
            drops++;
        }

        return results;
    }

    /// <summary>Apply loot to a wallet via the transaction pipeline.</summary>
    public void ApplyLoot(
        ref Wallet wallet,
        List<CurrencyCost> loot,
        TransactionProcessor txn,
        string source)
    {
        foreach (var drop in loot)
            txn.Earn(ref wallet, drop.CurrencyId, drop.Amount, source);
    }

    private DropEntry WeightedPick(List<DropEntry> entries, float totalWeight)
    {
        float roll = (float)(_rng.NextDouble() * totalWeight);
        float cumulative = 0;

        foreach (var entry in entries)
        {
            cumulative += entry.Weight;
            if (roll <= cumulative) return entry;
        }

        return entries[^1];
    }
}
```

### Example Drop Table (JSON)

```json
{
  "TableId": "goblin_loot",
  "Entries": [
    { "CurrencyId": "gold", "MinAmount": 5, "MaxAmount": 15, "Weight": 10, "DropChance": 1.0 },
    { "CurrencyId": "wood", "MinAmount": 1, "MaxAmount": 3, "Weight": 5, "DropChance": 0.4 },
    { "CurrencyId": "gems", "MinAmount": 1, "MaxAmount": 1, "Weight": 1, "DropChance": 0.05 }
  ],
  "GuaranteedDrops": 1,
  "MaxDrops": 3
}
```

---

## 10 — Economy Sinks & Faucets

Tracking where money comes from (faucets) and where it goes (sinks) is critical for balance. Without this, your economy will either inflate into meaninglessness or deflate into frustration.

### Monitoring System

```csharp
/// <summary>
/// ECS system that listens to TransactionEvents and tracks economy health.
/// Run this in debug/analytics builds.
/// </summary>
public class EconomyMonitorSystem
{
    private readonly Dictionary<string, FaucetSinkData> _data = new();

    public void ProcessEvent(in TransactionEvent evt)
    {
        if (!_data.TryGetValue(evt.CurrencyId, out var data))
        {
            data = new FaucetSinkData();
            _data[evt.CurrencyId] = data;
        }

        if (evt.Type == TransactionType.Earn)
        {
            data.TotalEarned += evt.Amount;
            data.FaucetBreakdown.TryGetValue(evt.Source, out long prev);
            data.FaucetBreakdown[evt.Source] = prev + evt.Amount;
        }
        else if (evt.Type == TransactionType.Spend)
        {
            data.TotalSpent += evt.Amount;
            data.SinkBreakdown.TryGetValue(evt.Source, out long prev);
            data.SinkBreakdown[evt.Source] = prev + evt.Amount;
        }
    }

    /// <summary>Dump a human-readable economy report.</summary>
    public string GetReport(string currencyId)
    {
        if (!_data.TryGetValue(currencyId, out var data))
            return $"No data for {currencyId}";

        var sb = new StringBuilder();
        sb.AppendLine($"=== Economy Report: {currencyId} ===");
        sb.AppendLine($"Total Earned: {data.TotalEarned:N0}");
        sb.AppendLine($"Total Spent:  {data.TotalSpent:N0}");
        sb.AppendLine($"Net Flow:     {data.TotalEarned - data.TotalSpent:N0}");
        sb.AppendLine($"Ratio (Sink/Faucet): {(data.TotalEarned > 0 ? (float)data.TotalSpent / data.TotalEarned : 0):F2}");
        sb.AppendLine();

        sb.AppendLine("--- Faucets (income sources) ---");
        foreach (var (source, amount) in data.FaucetBreakdown.OrderByDescending(x => x.Value))
            sb.AppendLine($"  {source}: {amount:N0} ({100f * amount / data.TotalEarned:F1}%)");

        sb.AppendLine("--- Sinks (spending) ---");
        foreach (var (source, amount) in data.SinkBreakdown.OrderByDescending(x => x.Value))
            sb.AppendLine($"  {source}: {amount:N0} ({100f * amount / data.TotalSpent:F1}%)");

        return sb.ToString();
    }
}

public class FaucetSinkData
{
    public long TotalEarned;
    public long TotalSpent;
    public Dictionary<string, long> FaucetBreakdown = new();
    public Dictionary<string, long> SinkBreakdown = new();
}
```

### Common Economy Patterns

| Pattern | Type | Description | Example |
|---------|------|-------------|---------|
| **Enemy bounty** | Faucet | Primary income in TD/action games | Kill goblin → +10 gold |
| **Resource gathering** | Faucet | Primary in survival games | Chop tree → +5 wood |
| **Wave bonus** | Faucet | Bonus for completing a wave | Wave 5 clear → +200 gold |
| **Interest** | Faucet | % bonus on held currency between waves | 5% on gold at wave end |
| **Shop purchase** | Sink | Player buys items/towers | Buy arrow tower → -100 gold |
| **Upgrade** | Sink | Improve existing items/buildings | Upgrade tower lv2 → -150 gold |
| **Repair** | Sink | Restore durability/health | Repair wall → -30 wood |
| **Tax/tithe** | Sink | Passive drain per time period | -5 gold/day maintenance |
| **Crafting** | Sink | Resources consumed to make items | 3 iron + 2 wood → Iron Sword |
| **Death penalty** | Sink | Lose % of currency on death | Die → lose 10% gold |

### Balance Guidelines

```
HEALTHY ECONOMY:
- Sink/Faucet ratio: 0.6 - 0.85
  (Players should spend 60-85% of what they earn)
- If < 0.5: Players hoard → no tension, economy meaningless
- If > 0.9: Players feel broke → frustrating, progress stalls

TOWER DEFENSE:
- Tower placement costs should use ~70% of wave income
- Upgrades should use ~20% of wave income
- ~10% for interest/saving buffer
- Selling towers should refund 50-70% (not 100% — or optimal play is constant sell/rebuy)

SURVIVAL:
- Resource gather rates: enough for basic survival in ~60% of playtime
- Crafting costs: mid-tier items require 2-3 gather sessions
- Trading: NPC prices should be 2-3x crafting cost (trade is convenience, not optimal)
```

---

## 11 — Save/Load Integration

Economy state needs to persist. Integrate with [G10 §3 Save/Load](./G10_custom_game_systems.md).

```csharp
/// <summary>
/// Serializable economy state for save files.
/// </summary>
public record EconomySaveData(
    Dictionary<string, int> WalletBalances,
    Dictionary<string, int> ShopStock,         // shopId:itemId → stock
    Dictionary<string, int> TradeUses,          // tradeId → times used
    Dictionary<string, int> ReputationLevels,
    Dictionary<string, long> TotalEarned,
    Dictionary<string, long> TotalSpent
);

public static class EconomySerialization
{
    public static EconomySaveData Capture(
        in Wallet playerWallet,
        ShopProcessor shopProcessor,
        BarterSystem? barterSystem,
        Reputation? reputation,
        TransactionProcessor transactions)
    {
        return new EconomySaveData(
            new Dictionary<string, int>(playerWallet.Balances),
            shopProcessor.CaptureStockState(),   // You'd add this method
            barterSystem?.CaptureTradeUses() ?? new(),
            reputation?.Levels != null ? new Dictionary<string, int>(reputation.Value.Levels) : new(),
            transactions.GetSnapshot().TotalEarned,
            transactions.GetSnapshot().TotalSpent
        );
    }

    public static void Restore(
        ref Wallet playerWallet,
        ShopProcessor shopProcessor,
        BarterSystem? barterSystem,
        ref Reputation reputation,
        EconomySaveData data,
        CurrencyRegistry registry)
    {
        // Restore wallet
        foreach (var (id, amount) in data.WalletBalances)
            WalletOps.Set(ref playerWallet, id, amount, registry);

        // Restore shop stock
        shopProcessor.RestoreStockState(data.ShopStock);

        // Restore trade uses
        barterSystem?.RestoreTradeUses(data.TradeUses);

        // Restore reputation
        reputation = new Reputation(new Dictionary<string, int>(data.ReputationLevels));
    }

    public static string ToJson(EconomySaveData data) =>
        JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });

    public static EconomySaveData FromJson(string json) =>
        JsonSerializer.Deserialize<EconomySaveData>(json)!;
}
```

---

## 12 — UI Integration

Economy UI is one of the most player-facing systems. Get it right.

### HUD Currency Display

```csharp
/// <summary>
/// Renders currency balances in the HUD. Supports animated count-up on changes.
/// </summary>
public class CurrencyHUD
{
    private readonly CurrencyRegistry _registry;
    private readonly Dictionary<string, AnimatedCounter> _counters = new();

    public CurrencyHUD(CurrencyRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>Call when a TransactionEvent fires to animate the change.</summary>
    public void OnTransaction(in TransactionEvent evt)
    {
        if (!_counters.TryGetValue(evt.CurrencyId, out var counter))
        {
            counter = new AnimatedCounter();
            _counters[evt.CurrencyId] = counter;
        }
        counter.TargetValue += evt.Type == TransactionType.Earn ? evt.Amount : -evt.Amount;
        counter.FlashTimer = 0.5f; // Flash for half a second
    }

    public void Update(float dt)
    {
        foreach (var counter in _counters.Values)
        {
            // Smooth count-up/down animation
            float diff = counter.TargetValue - counter.DisplayValue;
            if (Math.Abs(diff) < 1)
            {
                counter.DisplayValue = counter.TargetValue;
            }
            else
            {
                // Count faster when diff is larger
                float speed = Math.Max(Math.Abs(diff) * 5f, 50f);
                counter.DisplayValue += Math.Sign(diff) * speed * dt;
            }

            counter.FlashTimer = Math.Max(0, counter.FlashTimer - dt);
        }
    }

    public void Draw(SpriteBatch batch, SpriteFont font, Vector2 startPos)
    {
        var pos = startPos;
        foreach (var def in _registry.All)
        {
            if (!def.ShowInHUD) continue;

            _counters.TryGetValue(def.Id, out var counter);
            int displayVal = (int)(counter?.DisplayValue ?? 0);

            // Flash color when value changes
            Color color = (counter?.FlashTimer > 0)
                ? (counter.TargetValue > counter.DisplayValue ? Color.LimeGreen : Color.Red)
                : Color.White;

            // Draw: [icon] 1,234
            // (Icon drawing omitted — depends on your sprite system)
            batch.DrawString(font, $"{def.DisplayName}: {displayVal:N0}", pos, color);
            pos.Y += 24;
        }
    }
}

public class AnimatedCounter
{
    public float DisplayValue;
    public float TargetValue;
    public float FlashTimer;
}
```

### Shop UI Helpers

```csharp
/// <summary>
/// Format a price for display with color coding.
/// </summary>
public static class PriceDisplay
{
    /// <summary>Format with discount indicator.</summary>
    public static string Format(int finalPrice, int basePrice, string currencyName)
    {
        if (finalPrice < basePrice)
            return $"{finalPrice:N0} {currencyName} (was {basePrice:N0}, {DiscountPercent(basePrice, finalPrice)}% off)";
        if (finalPrice > basePrice)
            return $"{finalPrice:N0} {currencyName} (+{MarkupPercent(basePrice, finalPrice)}%)";
        return $"{finalPrice:N0} {currencyName}";
    }

    /// <summary>Color for affordability.</summary>
    public static Color AffordColor(bool canAfford) =>
        canAfford ? Color.White : new Color(180, 60, 60);

    /// <summary>Color for price comparison.</summary>
    public static Color PriceColor(int finalPrice, int basePrice) =>
        finalPrice < basePrice ? Color.LimeGreen :
        finalPrice > basePrice ? Color.IndianRed :
        Color.White;

    private static int DiscountPercent(int basePrice, int finalPrice) =>
        (int)(100f * (basePrice - finalPrice) / basePrice);

    private static int MarkupPercent(int basePrice, int finalPrice) =>
        (int)(100f * (finalPrice - basePrice) / basePrice);
}
```

---

## 13 — Economy Tuning Reference

### Quick-Start Values by Genre

| Parameter | Tower Defense | Survival | RPG |
|-----------|-------------|----------|-----|
| Starting gold | 200-500 | 0-50 | 100-300 |
| Enemy kill reward | 5-20 | 0-5 | 10-50 |
| Cheapest tower/item | 50-100 | N/A (crafted) | 25-75 |
| Mid-tier upgrade | 150-300 | 50-150 (resources) | 200-500 |
| End-game purchase | 800-2000 | 500+ (resources) | 5000-20000 |
| Sell-back ratio | 50-70% | 30-50% | 40-60% |
| Interest rate | 3-10%/wave | N/A | N/A |
| Restock timer | N/A | 3-10 min | 30-60 min |
| Sink/faucet target | 0.70-0.85 | 0.50-0.70 | 0.60-0.80 |

### Economy Red Flags

| Symptom | Likely Cause | Fix |
|---------|-------------|-----|
| Players always have max gold | Sinks too weak/few | Add upgrade tiers, consumables, cosmetics |
| Players constantly broke | Faucets too low or sink prices too high | Increase enemy bounties, reduce base costs |
| One item dominates purchases | That item is underpriced or overpowered | Raise price or nerf stats |
| Players never sell items | Sell price too low | Raise sell ratio to 40-50% |
| Inflation in late game | Not enough late-game sinks | Add prestige upgrades, expensive consumables |
| Optimal play = hoarding | Interest too high or upgrades not impactful | Cap interest, make upgrades more powerful |
| Players feel "stuck" | Income cliff between tiers | Add medium faucets (quests, exploration rewards) |

### Formulas

```
Enemy bounty scaling (TD):
  bounty = base + (base × waveScaling × (wave - 1))
  e.g., base=10, scaling=0.15: wave 10 → 10 + (10 × 0.15 × 9) = 23 gold

Tower cost scaling:
  upgrade_cost(level) = base_cost × (1.5 ^ level)
  e.g., base=100: lv1=100, lv2=150, lv3=225, lv4=337

Interest (BTD-style):
  interest = min(gold × rate, cap)
  e.g., 1000 gold × 5% = 50 (cap: 200)

Sell refund:
  refund = total_invested × sell_ratio
  e.g., 100 (place) + 150 (upgrade) = 250 × 0.6 = 150 refund

Dynamic pricing (supply-based):
  price = base × (1 + (maxStock - currentStock) / maxStock × 0.5)
  e.g., base=100, maxStock=10, currentStock=3: 100 × 1.35 = 135
```

---

## Putting It Together

### Minimal Tower Defense Setup

```csharp
// In your Game.Initialize() or scene setup:

// 1. Register currencies
var currencyRegistry = new CurrencyRegistry();
currencyRegistry.Register(new CurrencyDef("gold", "Gold", "icon_gold", 999999, false, true));

// 2. Set up transaction pipeline
var transactions = new TransactionProcessor(currencyRegistry, world);

// 3. Register tower costs
var towerEcon = new TowerEconomySystem(transactions, currencyRegistry);
towerEcon.RegisterTower(new TowerCostDef("arrow", "gold", 100, new[] { 150, 250 }, 0.6f));
towerEcon.RegisterTower(new TowerCostDef("cannon", "gold", 200, new[] { 300, 500 }, 0.5f));

// 4. Initialize player wallet
var playerWallet = new Wallet();
WalletOps.Set(ref playerWallet, "gold", 300, currencyRegistry); // Starting gold

// 5. On enemy kill:
towerEcon.OnEnemyKilled(ref playerWallet, enemyBounty, currentWave);

// 6. On tower place:
if (towerEcon.TryPlaceTower(ref playerWallet, "arrow", towerEntity))
    SpawnTower("arrow", gridPos);

// 7. On wave end:
towerEcon.OnWaveEnd(ref playerWallet, towerEconomy);
```

### Minimal Survival Setup

```csharp
// 1. Register resource currencies
var currencyRegistry = new CurrencyRegistry();
currencyRegistry.Register(new CurrencyDef("wood", "Wood", "icon_wood", 9999, false, true));
currencyRegistry.Register(new CurrencyDef("stone", "Stone", "icon_stone", 9999, false, true));
currencyRegistry.Register(new CurrencyDef("iron", "Iron", "icon_iron", 9999, false, false));
currencyRegistry.Register(new CurrencyDef("gold", "Gold", "icon_gold", 99999, false, true));

// 2. Transaction pipeline
var transactions = new TransactionProcessor(currencyRegistry, world);

// 3. Barter trades
var barter = new BarterSystem(transactions, currencyRegistry);
barter.RegisterTrade(new TradeDef(
    "wood_for_gold",
    "5 Wood → 2 Gold",
    new() { new("wood", 5) },
    new() { new("gold", 2) },
    -1, false
));

// 4. On resource gather (e.g., player hits a tree):
transactions.Earn(ref playerWallet, "wood", 3, "gather:tree");

// 5. On trade with NPC:
barter.ExecuteTrade(ref playerWallet, "wood_for_gold");
```

---

*Economy systems make or break game feel. A perfectly balanced combat system feels meaningless if there's nothing worth spending loot on. Build the economy pipeline early, track your faucets and sinks from day one, and iterate based on data — not gut feeling.*
