using UnityEngine;
using Unity.Netcode;

public class TreeChoppable : MonoBehaviour
{
    [Header("Health")]
    [SerializeField] private float maxHitPoints = 3f;
    [SerializeField] private bool destroyWhenDepleted = true;
    [SerializeField] private float destroyDelay = 0f;

    [Header("Optional Drops")]
    [SerializeField] private PickableItem dropPrefab;
    [SerializeField] private int dropAmount = 1;
    [SerializeField] private Transform dropPoint;
    [SerializeField] private float dropImpulse = 1.8f;
    [SerializeField] private bool spawnAsSingleStack = false;
    [SerializeField] private float dropScatterRadius = 0.25f;

    [Header("Optional Visual")]
    [SerializeField] private GameObject choppedReplacement;

    [Header("Debug")]
    [SerializeField] private bool logHits;
    [SerializeField] private bool preventRapidDuplicateHits = true;
    [SerializeField] private float minHitIntervalSeconds = 0.3f;

    private float currentHitPoints;
    private bool isDepleted;
    private float lastAcceptedHitTime = -999f;
    private int lastAttackerInstanceId = int.MinValue;

    public bool IsDepleted => isDepleted;
    public float CurrentHitPoints => Mathf.Max(0f, currentHitPoints);

    private void Awake()
    {
        maxHitPoints = Mathf.Max(1f, maxHitPoints);
        currentHitPoints = maxHitPoints;

        if (choppedReplacement != null)
        {
            choppedReplacement.SetActive(false);
        }
    }

    public bool ApplyAxeHit(float damage, GameObject attacker = null)
    {
        if (isDepleted || damage <= 0f)
        {
            return false;
        }

        if (preventRapidDuplicateHits)
        {
            float now = Time.time;
            int attackerInstanceId = attacker != null ? attacker.GetInstanceID() : int.MinValue;
            bool sameAttacker = attacker == null || attackerInstanceId == lastAttackerInstanceId;
            float minInterval = Mathf.Max(0f, minHitIntervalSeconds);

            if (sameAttacker && (now - lastAcceptedHitTime) < minInterval)
            {
                if (logHits)
                {
                    Debug.Log(
                        $"Tree '{name}' duplicate hit diabaikan. " +
                        $"delta={(now - lastAcceptedHitTime):0.000}s, min={minInterval:0.000}s");
                }

                return false;
            }

            lastAcceptedHitTime = now;
            lastAttackerInstanceId = attackerInstanceId;
        }

        currentHitPoints = Mathf.Max(0f, currentHitPoints - damage);

        if (logHits)
        {
            Debug.Log($"Tree '{name}' kena kapak. HP: {currentHitPoints:0.##}/{maxHitPoints:0.##}");
        }

        if (currentHitPoints <= 0f)
        {
            this.OnTreeChopped(attacker, spawnDrop: true);
        }

        return true;
    }

    public void ApplyReplicatedHit(float damage, bool forceDepleted)
    {
        if (isDepleted)
        {
            return;
        }

        currentHitPoints = Mathf.Max(0f, currentHitPoints - Mathf.Max(0f, damage));
        if (forceDepleted || currentHitPoints <= 0f)
        {
            this.OnTreeChopped(attacker: null, spawnDrop: false);
        }
    }

    private void OnTreeChopped(GameObject attacker, bool spawnDrop)
    {
        if (isDepleted)
        {
            return;
        }

        isDepleted = true;
        if (spawnDrop)
        {
            this.SpawnDrop();
        }

        if (choppedReplacement != null)
        {
            choppedReplacement.transform.position = transform.position;
            choppedReplacement.transform.rotation = transform.rotation;
            choppedReplacement.SetActive(true);
        }

        if (destroyWhenDepleted)
        {
            if (destroyDelay <= 0f)
            {
                Destroy(gameObject);
            }
            else
            {
                Destroy(gameObject, destroyDelay);
            }
        }
    }

    private void SpawnDrop()
    {
        if (dropPrefab == null)
        {
            return;
        }

        int totalAmount = Mathf.Max(1, dropAmount);
        int spawnCount = spawnAsSingleStack ? 1 : totalAmount;
        int amountPerDrop = spawnAsSingleStack ? totalAmount : 1;
        Vector3 basePosition = dropPoint != null
            ? dropPoint.position
            : transform.position + (Vector3.up * 0.5f);

        for (int i = 0; i < spawnCount; i++)
        {
            Vector2 randomOffset2D = Random.insideUnitCircle * Mathf.Max(0f, dropScatterRadius);
            Vector3 spawnPosition = basePosition + new Vector3(randomOffset2D.x, 0f, randomOffset2D.y);
            PickableItem dropped = Instantiate(dropPrefab, spawnPosition, Quaternion.identity);
            dropped.gameObject.SetActive(true);
            dropped.amount = Mathf.Max(1, amountPerDrop);

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && NetworkManager.Singleton.IsServer)
            {
                NetworkObject droppedNetObj = dropped.GetComponent<NetworkObject>();
                if (droppedNetObj != null && !droppedNetObj.IsSpawned)
                {
                    droppedNetObj.Spawn(true);
                }
            }

            Rigidbody rb = dropped.GetComponent<Rigidbody>();
            if (rb != null)
            {
                Vector3 randomPush = transform.forward + Vector3.up + new Vector3(randomOffset2D.x, 0f, randomOffset2D.y);
                rb.AddForce(randomPush.normalized * dropImpulse, ForceMode.VelocityChange);
            }
        }
    }
}
