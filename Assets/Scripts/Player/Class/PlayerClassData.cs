using UnityEngine;

/// <summary>
/// Data stat per kelas pemain (Survivor / Hunter / Firekeeper).
/// Tipe ini adalah ScriptableObject agar tiap kelas bisa di-tune
/// dari Inspector tanpa mengubah kode, dan Photon Fusion cukup
/// mereferensikan id PlayerClassType di Networked property.
/// </summary>
[CreateAssetMenu(menuName = "ProjectMultiplayer/Player Class", fileName = "PlayerClass_New", order = 110)]
public class PlayerClassData : ScriptableObject
{
    [Header("Identitas")]
    [SerializeField] private string classId = "survivor";
    [SerializeField] private string displayName = "Survivor";
    [SerializeField, TextArea(2, 4)] private string description = "Frontliner dengan HP tinggi dan gathering 1.5x.";
    [SerializeField] private Color tintColor = new Color(0.62f, 0.66f, 0.70f, 1f);
    [SerializeField] private Sprite icon;

    [Header("Stat dasar")]
    [SerializeField, Min(1f)] private float maxHealth = 100f;
    [SerializeField, Min(0.5f)] private float walkSpeedMultiplier = 1f;
    [SerializeField, Min(0.5f)] private float runSpeedMultiplier = 1f;

    [Header("Survival modifier (1 = normal, <1 = lebih lambat decay)")]
    [SerializeField, Range(0.25f, 1.5f)] private float hungerDecayMultiplier = 1f;
    [SerializeField, Range(0.25f, 1.5f)] private float thirstDecayMultiplier = 1f;
    [SerializeField, Range(0.25f, 1.5f)] private float warmthDecayMultiplier = 1f;
    [SerializeField, Range(0.25f, 1.5f)] private float fallDamageMultiplier = 1f;

    [Header("Gathering & Utility")]
    [SerializeField, Range(0.5f, 3f)] private float gatheringMultiplier = 1f;
    [SerializeField, Range(0.5f, 2f)] private float campfireFuelMultiplier = 1f;
    [SerializeField, Range(0.5f, 3f)] private float healingReceivedMultiplier = 1f;
    [SerializeField, Range(0.25f, 2f)] private float animalAggroRadiusMultiplier = 1f;

    [Header("Combat modifier")]
    [SerializeField, Range(0.5f, 2f)] private float meleeDamageMultiplier = 1f;
    [SerializeField, Range(0.5f, 2f)] private float rangedDamageMultiplier = 1f;

    [Header("Equipment affinity (soft lock)")]
    [Tooltip("Tipe item equipment yang mendapat damage bonus dari kelas ini. Pakai ItemType enum.")]
    [SerializeField] private ItemType[] preferredItemTypes = System.Array.Empty<ItemType>();
    [SerializeField, Range(0f, 1f)] private float preferredBonus = 0.15f;
    [SerializeField, Range(0f, 0.5f)] private float nonPreferredPenalty = 0.15f;

    [Header("Tagging")]
    [Tooltip("Tag fallback yang dipakai di scene bila object memakai Tag Override dan tidak ketemu di woodTagNames dll.")]
    [SerializeField] private string classTag = "Survivor";

    public string ClassId => classId;
    public string DisplayName => displayName;
    public string Description => description;
    public Color TintColor => tintColor;
    public Sprite Icon => icon;
    public float MaxHealth => maxHealth;
    public float WalkSpeedMultiplier => walkSpeedMultiplier;
    public float RunSpeedMultiplier => runSpeedMultiplier;
    public float HungerDecayMultiplier => hungerDecayMultiplier;
    public float ThirstDecayMultiplier => thirstDecayMultiplier;
    public float WarmthDecayMultiplier => warmthDecayMultiplier;
    public float FallDamageMultiplier => fallDamageMultiplier;
    public float GatheringMultiplier => gatheringMultiplier;
    public float CampfireFuelMultiplier => campfireFuelMultiplier;
    public float HealingReceivedMultiplier => healingReceivedMultiplier;
    public float AnimalAggroRadiusMultiplier => animalAggroRadiusMultiplier;
    public float MeleeDamageMultiplier => meleeDamageMultiplier;
    public float RangedDamageMultiplier => rangedDamageMultiplier;
    public ItemType[] PreferredItemTypes => preferredItemTypes;
    public float PreferredBonus => preferredBonus;
    public float NonPreferredPenalty => nonPreferredPenalty;
    public string ClassTag => classTag;

    /// <summary>Damage multiplier final (preferred bonus / non-preferred penalty) untuk itemType tertentu.</summary>
    public float ResolveDamageMultiplier(ItemType itemType)
    {
        if (preferredItemTypes == null || preferredItemTypes.Length == 0)
        {
            return meleeDamageMultiplier;
        }

        for (int i = 0; i < preferredItemTypes.Length; i++)
        {
            if (preferredItemTypes[i] == itemType)
            {
                return meleeDamageMultiplier * (1f + preferredBonus);
            }
        }

        return meleeDamageMultiplier * (1f - nonPreferredPenalty);
    }
}
