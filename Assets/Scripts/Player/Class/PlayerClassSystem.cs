using System.Collections.Generic;
using UnityEngine;
using Fusion;

/// <summary>
/// Sistem kelas pemain — komponen NetworkBehaviour di tiap prefab player.
/// Class hanya boleh dipilih sebelum match (lobby), dan di-LOCK selama match
/// sesuai keputusan Carlo. Tidak ada 'swap mid-game'.
///
/// Sumber data: PlayerClassData (ScriptableObject). Dipilih berdasarkan
/// PlayerClassType (existing) yang di-broadcast lewat [Networked].
///
/// File ini:
/// 1. Registrasi ScriptableObject per class (singleton lookup)
/// 2. Set classId di state authority, broadcast ke semua peer
/// 3. Apply stat ke PlayerSurvivalSystem, PlayerMovement, dll saat spawn
/// 4. Resolve damage multiplier dengan equipment affinity
/// </summary>
[DisallowMultipleComponent]
public class PlayerClassSystem : NetworkBehaviour
{
    [Header("Database")]
    [Tooltip("Semua PlayerClassData yang tersedia. Dipakai server untuk resolve ClassId -> ScriptableObject.")]
    [SerializeField] private List<PlayerClassData> registeredClasses = new List<PlayerClassData>();

    [Header("Default")]
    [SerializeField] private PlayerClassType defaultClassId = PlayerClassType.Lumberjack;

    [Header("Cache runtime")]
    [SerializeField] private PlayerSurvivalSystem survivalSystem;
    [SerializeField] private MonoBehaviour movementComponent; // opsional: class dengan field walkSpeed/runSpeed

    /// <summary>Id class milik player ini, di-set di state authority, di-broadcast via Networked.</summary>
    [Networked] public PlayerClassType ClassId { get; private set; }

    /// <summary>Data class saat ini. Null bila ClassId == Unassigned atau asset belum diregistrasi.</summary>
    public PlayerClassData CurrentClass { get; private set; }

    /// <summary>True bila class sudah di-apply ke survivalSystem (HP dst).</summary>
    public bool IsClassApplied { get; private set; }

    private static readonly Dictionary<PlayerClassType, PlayerClassData> classRegistry = new Dictionary<PlayerClassType, PlayerClassData>();
    private static bool registryBuilt;

    private void Awake()
    {
        if (survivalSystem == null) survivalSystem = GetComponent<PlayerSurvivalSystem>();
        if (movementComponent == null) movementComponent = GetComponent("PlayerMovement") as MonoBehaviour;
        BuildRegistryIfNeeded();
    }

    public override void Spawned()
    {
        base.Spawned();
        BuildRegistryIfNeeded();

        if (HasStateAuthority && ClassId == default(PlayerClassType))
        {
            ClassId = defaultClassId;
        }

        // Resolve dan apply di SEMUA peer agar UI/footstep/dll konsisten.
        ApplyCurrentClass();
    }

    private static void BuildRegistryIfNeeded()
    {
        if (registryBuilt) return;
        registryBuilt = true;
        classRegistry.Clear();

        // Cari semua PlayerClassData di Resources untuk class bawaan (Survivor/Hunter/Firekeeper).
        var all = Resources.FindObjectsOfTypeAll<PlayerClassData>();
        if (all != null)
        {
            for (int i = 0; i < all.Length; i++)
            {
                var c = all[i];
                if (c == null || string.IsNullOrEmpty(c.ClassId)) continue;
                if (System.Enum.TryParse<PlayerClassType>(c.ClassId, true, out var id))
                {
                    classRegistry[id] = c;
                }
            }
        }
    }

    /// <summary>Dipanggil dari lobby/UI: state authority set class sebelum match start.</summary>
    public void RequestSetClass(PlayerClassType newClassId)
    {
        if (!HasStateAuthority)
        {
            Debug.LogWarning("[PlayerClassSystem] RequestSetClass hanya boleh di state authority.");
            return;
        }
        if (newClassId == default(PlayerClassType)) return;
        if (ClassId != default(PlayerClassType))
        {
            Debug.LogWarning("[PlayerClassSystem] Class sudah ter-LOCK. Wipe atau restart match untuk ganti.");
            return;
        }
        ClassId = newClassId;
        ApplyCurrentClass();
    }

    private void ApplyCurrentClass()
    {
        if (ClassId == default(PlayerClassType)) return;

        CurrentClass = ResolveClassData(ClassId);
        if (CurrentClass == null)
        {
            Debug.LogWarning("[PlayerClassSystem] Tidak ada PlayerClassData untuk id=" + ClassId);
            return;
        }

        // 1) Apply maxHealth ke survival system (Authority saja yang boleh ubah HP limit).
        if (survivalSystem != null && HasStateAuthority)
        {
            survivalSystem.SetMaxHealth(CurrentClass.MaxHealth);
            survivalSystem.SetStatMultipliers(
                CurrentClass.HungerDecayMultiplier,
                CurrentClass.ThirstDecayMultiplier,
                CurrentClass.WarmthDecayMultiplier,
                CurrentClass.FallDamageMultiplier);
        }

        // 2) Apply movement speed multiplier (semua peer).
        if (movementComponent != null)
        {
            ApplyMovementMultipliers(movementComponent, CurrentClass.WalkSpeedMultiplier, CurrentClass.RunSpeedMultiplier);
        }

        IsClassApplied = true;
        Debug.Log("[PlayerClassSystem] Applied class " + CurrentClass.DisplayName + " to " + gameObject.name);
    }

    /// <summary>Mengembalikan ScriptableObject class. Urutan lookup: registered list -> static registry.</summary>
    public PlayerClassData ResolveClassData(PlayerClassType id)
    {
        if (id == default(PlayerClassType)) return null;

        if (registeredClasses != null)
        {
            for (int i = 0; i < registeredClasses.Count; i++)
            {
                var c = registeredClasses[i];
                if (c == null) continue;
                if (System.Enum.TryParse<PlayerClassType>(c.ClassId, true, out var cid) && cid == id)
                {
                    return c;
                }
            }
        }

        classRegistry.TryGetValue(id, out var found);
        return found;
    }

    /// <summary>Multiplikasi damage akhir untuk itemType tertentu berdasarkan class affinity.</summary>
    public float ResolveDamageMultiplier(ItemType itemType, float baseDamage)
    {
        if (CurrentClass == null) return baseDamage;
        if (itemType == ItemType.Bow || itemType == ItemType.Spear)
        {
            return baseDamage * CurrentClass.RangedDamageMultiplier;
        }
        return baseDamage * CurrentClass.ResolveDamageMultiplier(itemType);
    }

    /// <summary>Damage yang diterima (untuk class dengan damage reduction pasif, mis. Survivor fall).</summary>
    public float ResolveIncomingDamage(float rawDamage, DamageCategory category)
    {
        if (CurrentClass == null) return rawDamage;
        if (category == DamageCategory.Fall)
        {
            return rawDamage * CurrentClass.FallDamageMultiplier;
        }
        if (category == DamageCategory.Melee)
        {
            // Class support (Firekeeper) tidak punya damage reduction default di sini.
            return rawDamage;
        }
        return rawDamage;
    }

    private static void ApplyMovementMultipliers(MonoBehaviour target, float walkMul, float runMul)
    {
        if (target == null) return;
        var t = target.GetType();
        var walkField = t.GetField("walkSpeed", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        var runField = t.GetField("runSpeed", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (walkField != null && walkField.FieldType == typeof(float))
        {
            float v = (float)walkField.GetValue(target);
            walkField.SetValue(target, v * walkMul);
        }
        if (runField != null && runField.FieldType == typeof(float))
        {
            float v = (float)runField.GetValue(target);
            runField.SetValue(target, v * runMul);
        }
    }

    public enum DamageCategory { Fall, Melee, Animal, Projectile, Environment, Cold, Hunger }
}
