# G11 — Programming Principles
> **Category:** Guide · **Related:** [G12 Design Patterns](./G12_design_patterns.md) · [E1 Architecture Overview](../../monogame-arch/architecture/E1_architecture_overview.md) · [E5 AI Workflow](../ai-workflow/E5_ai_workflow.md)

---

Foundational principles applied to game development with MonoGame + Arch ECS.

---

## SOLID Principles

### Single Responsibility Principle (SRP)

One class should have one reason to change. Prevents the "God Object" anti-pattern.

**BAD — God Object:**
```csharp
public class Player
{
    private int _health = 100;
    private List<Item> _inventory = new();

    public void Update()
    {
        HandleMovement();
        HandleCombat();
        UpdateUI();
        CheckAchievements();
        AutoSave();
    }
}
```

**GOOD — Separated Responsibilities:**
```csharp
// HealthComponent.cs — only health management
public struct HealthComponent
{
    public int MaxHealth;
    public int CurrentHealth;
}

// In Arch ECS, separate systems handle separate concerns:
// PlayerMovementSystem — only movement
// CombatSystem — only damage/combat
// UISystem — only HUD updates
```

Each component has exactly one reason to change. HealthComponent changes only when health mechanics change.

---

### Open/Closed Principle (OCP)

Open for extension, closed for modification. Add new behavior without changing existing code.

**BAD — Must modify to add weapons:**
```csharp
public int CalculateDamage(string weaponType) => weaponType switch
{
    "sword" => 10, "bow" => 5, "staff" => 15,
    _ => 0 // Must add new cases for each weapon!
};
```

**GOOD — Extend via interface:**
```csharp
public interface IWeapon
{
    int Damage { get; }
    float AttackSpeed { get; }
    void Attack(Entity target);
}

public class Sword : IWeapon { public int Damage => 10; /* ... */ }
public class MagicStaff : IWeapon { public int Damage => 15; /* ... */ }
```

**GOOD — Data-driven extension:**
```csharp
public class WeaponData
{
    public string Id { get; set; }
    public int Damage { get; set; }
    public float AttackSpeed { get; set; }
}
// New weapons = new JSON files, zero code changes
```

---

### Liskov Substitution Principle (LSP)

Subtypes must be substitutable for their base types without breaking behavior.

**BAD — Turret breaks Enemy contract:**
```csharp
public class StationaryTurret : Enemy
{
    public override void Move(Vector2 direction)
    {
        throw new NotImplementedException(); // Violates LSP!
    }
}
```

**GOOD — Separate interfaces for separate capabilities:**
```csharp
public interface IMoveable { void Move(Vector2 direction); }
public interface IAttacker { void Attack(Entity target); }

public class MobileEnemy : Entity, IMoveable, IAttacker { }
public class Turret : Entity, IAttacker { } // Only implements what it can do
```

---

### Interface Segregation Principle (ISP)

Many specific interfaces beat one bloated interface. Clients shouldn't implement methods they don't use.

```csharp
public interface IDamageable
{
    int CurrentHealth { get; }
    void TakeDamage(int amount);
}

public interface ISaveable
{
    Dictionary<string, object> GetSaveData();
    void LoadSaveData(Dictionary<string, object> data);
}

public interface IInteractable
{
    string InteractionPrompt { get; }
    void Interact(Entity interactor);
}

// Classes implement only what they need
public class Chest : Entity, IInteractable, ISaveable { }
public class Barrel : Entity, IDamageable { }
```

---

### Dependency Inversion Principle (DIP)

High-level modules depend on abstractions, not low-level details.

**GOOD — Depends on abstractions:**
```csharp
public interface ISaveService
{
    void Save(Dictionary<string, object> data);
    Dictionary<string, object> Load();
}

public class GameManager
{
    private readonly ISaveService _saveService;
    public GameManager(ISaveService saveService) => _saveService = saveService;
    public void SaveGame() => _saveService.Save(CollectSaveData());
}
// LocalFileSaveService, CloudSaveService both implement ISaveService
// Swap at construction time — GameManager never changes
```

---

## DRY, KISS, YAGNI

**DRY (Don't Repeat Yourself):** Duplicated *knowledge* (business rules, formulas, constants) must be unified. Duplicated code that happens to look similar but serves different purposes can stay separate — forcing premature abstraction creates coupling worse than duplication.

**KISS (Keep It Simple, Stupid):** Start with the simplest solution that works. Refactor when complexity is actually needed, not when it might be. John Carmack: *"It is hard for less experienced developers to appreciate how rarely architecting for future requirements turns out net-positive."*

**YAGNI (You Aren't Gonna Need It):** Start with hardcoded AI behaviors before building data-driven rule systems. The working prototype ships faster and informs better architecture if scaling becomes necessary.

---

## Composition Over Inheritance

Modern game architecture strongly favors composition. Inheritance is rigid — game entities need to change abilities at runtime and combine capabilities in unexpected ways.

**Use inheritance for:** True "is-a" relationships, shared implementation details, small stable hierarchies, framework extension points.

**Use composition for:** "has-a" relationships, runtime flexibility, abilities that change, combining capabilities from multiple sources.

In Arch ECS, composition is the default. Entities are just IDs. Components are pure data structs. Systems process component combinations. A player is `Position + Velocity + PlayerTag + Health + Inventory`. An enemy is `Position + Velocity + EnemyTag + Health + AIState`. Shared components (Position, Velocity, Health) work identically for both — no inheritance needed.
