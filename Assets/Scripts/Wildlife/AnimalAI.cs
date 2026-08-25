using System.Collections;
using UnityEngine;

/// <summary>
/// AI hewan arktik sederhana (non-networked v1): Idle / Wander / Flee / Chase / Attack / Dead.
/// Gerakan mengikuti medan via raycast ke bawah (kompatibel 9 tile taiga; JANGAN pakai
/// Terrain.activeTerrain karena hanya menunjuk tile pertama).
/// Damage dari pemain masuk lewat TakeDamage() (dipanggil PlayerAxeCombat.ProcessHit).
/// Predator melukai pemain via jalur networked FusionPlayerSurvival.ApplyDamageForStateAuthority
/// (fallback lokal PlayerSurvivalSystem untuk mode tanpa sesi).
/// Mati -> drop bahan makanan (default RawMeat) via WildlifeManager.SpawnPickables.
/// </summary>
public class AnimalAI : MonoBehaviour
{
    public enum State { Idle, Wander, Flee, Chase, Attack, Dead }

    [Header("Identitas")]
    public string speciesName = "Deer";

    [Header("Stat")]
    public float maxHealth = 60f;
    [SerializeField] private float walkSpeed = 1.7f;
    [SerializeField] private float runSpeed = 4.6f;
    [SerializeField] private float turnSpeedDegPerSec = 240f;

    [Header("Perilaku pasif (prey)")]
    [SerializeField] private bool isPredator = false;
    [SerializeField] private float fleeRadius = 9f;

    [Header("Perilaku predator")]
    [SerializeField] private float aggroRadius = 8f;
    [SerializeField] private float attackRange = 2.2f;
    [SerializeField] private float attackDamage = 12f;
    [SerializeField] private float attackIntervalSeconds = 1.5f;

    [Header("Wander")]
    [SerializeField] private float wanderRadius = 22f;
    [SerializeField] private float wanderRepickSeconds = 9f;

    [Header("Deteksi")]
    [SerializeField] private float detectIntervalSeconds = 0.3f;

    [Header("Drop saat mati")]
    [SerializeField] private ItemType meatItemType = ItemType.RawMeat;
    [SerializeField] private int meatDropAmount = 2;

    [Header("Bangkai")]
    [SerializeField] private float carcassSinkDelaySeconds = 12f;
    [SerializeField] private float carcassSinkDuration = 4f;

    public State CurrentState { get; private set; } = State.Idle;
    public float Health { get; private set; }

    public bool IsDead()
    {
        return CurrentState == State.Dead;
    }

    private Transform playerTarget;
    private Vector3 homePosition;
    private Vector3 wanderDestination;
    private float wanderTimer;
    private float detectTimer;
    private float attackCooldown;
    private bool dyingStarted;

    private void Start()
    {
        // Start (bukan Awake): WildlifeManager mengisi config SEGERA setelah AddComponent,
        // sehingga nilai akhir (maxHealth dll.) yang terpaksa.
        Health = maxHealth;
        homePosition = transform.position;
        wanderDestination = transform.position;
    }

    private void Update()
    {
        if (CurrentState == State.Dead)
        {
            return;
        }

        detectTimer -= Time.deltaTime;
        attackCooldown -= Time.deltaTime;
        if (detectTimer <= 0f)
        {
            detectTimer = detectIntervalSeconds;
            UpdatePlayerTarget();
            EvaluateTransitions();
        }

        switch (CurrentState)
        {
            case State.Idle:
                wanderTimer -= Time.deltaTime;
                if (wanderTimer <= 0f)
                {
                    PickNewWanderDestination();
                    CurrentState = State.Wander;
                }
                break;

            case State.Wander:
                StepToward(wanderDestination, walkSpeed);
                wanderTimer -= Time.deltaTime;
                if (HasArrived(wanderDestination) || wanderTimer <= 0f)
                {
                    CurrentState = State.Idle;
                    wanderTimer = Random.Range(1.5f, 4f);
                }
                break;

            case State.Flee:
                if (playerTarget != null)
                {
                    Vector3 away = transform.position - playerTarget.position;
                    away.y = 0f;
                    StepToward(transform.position + away.normalized * 6f, runSpeed);
                }
                break;

            case State.Chase:
                if (playerTarget == null)
                {
                    CurrentState = State.Wander;
                    break;
                }

                StepToward(playerTarget.position, runSpeed);
                if (GetFlatDistance(playerTarget.position) <= attackRange)
                {
                    CurrentState = State.Attack;
                }
                break;

            case State.Attack:
                if (playerTarget == null)
                {
                    CurrentState = State.Wander;
                    break;
                }

                FaceToward(playerTarget.position);
                if (GetFlatDistance(playerTarget.position) > attackRange * 1.25f)
                {
                    CurrentState = State.Chase;
                    break;
                }

                if (attackCooldown <= 0f)
                {
                    attackCooldown = attackIntervalSeconds;
                    // Prioritas jalur networked (sumber kebenaran HP player),
                    // fallback lokal untuk mode tanpa sesi.
                    var fusionVictim = playerTarget.GetComponentInParent<FusionPlayerSurvival>();
                    if (fusionVictim != null)
                    {
                        fusionVictim.ApplyDamageForStateAuthority(attackDamage, Fusion.PlayerRef.None);
                    }
                    else
                    {
                        PlayerSurvivalSystem victim = playerTarget.GetComponentInParent<PlayerSurvivalSystem>();
                        if (victim != null)
                        {
                            victim.ApplyDamage(attackDamage);
                        }
                    }
                }
                break;
        }
    }

    /// <summary>Dipanggil PlayerAxeCombat ketika kapak kena collider hewan.</summary>
    public void TakeDamage(float amount)
    {
        if (CurrentState == State.Dead || amount <= 0f)
        {
            return;
        }

        Health -= amount;
        if (Health <= 0f)
        {
            Die();
            return;
        }

        // Prey langsung kabur; predator membalas mengejar penyerang terdekat.
        UpdatePlayerTarget();
        if (!isPredator && playerTarget != null)
        {
            CurrentState = State.Flee;
        }
        else if (isPredator && playerTarget != null && CurrentState != State.Attack)
        {
            CurrentState = State.Chase;
        }
    }

    private void UpdatePlayerTarget()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, Mathf.Max(aggroRadius, fleeRadius) + 4f);
        float bestDistance = float.MaxValue;
        Transform best = null;
        foreach (Collider candidate in hits)
        {
            PlayerSurvivalSystem survival = candidate.GetComponentInParent<PlayerSurvivalSystem>();
            if (survival == null || !survival.gameObject.activeInHierarchy || survival.transform == transform)
            {
                continue;
            }

            float distance = GetFlatDistance(survival.transform.position);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = survival.transform;
            }
        }

        playerTarget = best;
    }

    private void EvaluateTransitions()
    {
        if (playerTarget == null)
        {
            if (CurrentState == State.Flee || CurrentState == State.Chase || CurrentState == State.Attack)
            {
                CurrentState = State.Wander;
                wanderTimer = wanderRepickSeconds;
            }

            return;
        }

        float distance = GetFlatDistance(playerTarget.position);
        if (isPredator)
        {
            if (distance <= aggroRadius && CurrentState != State.Chase && CurrentState != State.Attack)
            {
                CurrentState = State.Chase;
            }
        }
        else if (distance <= fleeRadius && CurrentState != State.Flee)
        {
            CurrentState = State.Flee;
        }
        else if (!isPredator && distance > fleeRadius * 1.7f && CurrentState == State.Flee)
        {
            CurrentState = State.Idle;
            wanderTimer = Random.Range(1f, 3f);
        }
    }

    private void PickNewWanderDestination()
    {
        Vector2 offset = Random.insideUnitCircle * wanderRadius;
        wanderDestination = homePosition + new Vector3(offset.x, 0f, offset.y);
        wanderTimer = wanderRepickSeconds;
    }

    private void StepToward(Vector3 destination, float speed)
    {
        Vector3 flat = destination - transform.position;
        flat.y = 0f;
        if (flat.sqrMagnitude < 0.0001f)
        {
            return;
        }

        Vector3 direction = flat.normalized;
        Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, turnSpeedDegPerSec * Time.deltaTime);

        Vector3 nextPosition = transform.position + direction * (speed * Time.deltaTime);
        if (TrySampleGround(nextPosition, out Vector3 grounded))
        {
            transform.position = grounded;
        }
    }

    private void FaceToward(Vector3 destination)
    {
        Vector3 flat = destination - transform.position;
        flat.y = 0f;
        if (flat.sqrMagnitude < 0.0001f)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, turnSpeedDegPerSec * Time.deltaTime);
    }

    private bool TrySampleGround(Vector3 sampleAt, out Vector3 grounded)
    {
        grounded = sampleAt;
        Vector3 origin = sampleAt + Vector3.up * 6f;
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 60f);
        float bestDistance = float.MaxValue;
        bool found = false;
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null || hit.collider.isTrigger)
            {
                continue;
            }

            // WAJIB skip collider diri sendiri (jebakan klasik: tanpa ini hewan
            // memanjat ke langit +-2 m/frame dengan berdiri di atas kepalanya).
            if (hit.collider.GetComponentInParent<AnimalAI>() == this)
            {
                continue;
            }

            // Tolak permukaan yang lebih tinggi dari posisi sekarang (kanopi/dahan):
            // hewan tidak boleh "naik" karena raycast.
            if (hit.point.y > sampleAt.y + 2.5f)
            {
                continue;
            }

            if (hit.distance < bestDistance)
            {
                bestDistance = hit.distance;
                grounded = new Vector3(sampleAt.x, hit.point.y, sampleAt.z);
                found = true;
            }
        }

        return found;
    }

    private float GetFlatDistance(Vector3 otherPosition)
    {
        Vector3 delta = transform.position - otherPosition;
        delta.y = 0f;
        return delta.magnitude;
    }

    private bool HasArrived(Vector3 destination)
    {
        return GetFlatDistance(destination) <= 1.2f;
    }

    private void Die()
    {
        CurrentState = State.Dead;
        if (!dyingStarted)
        {
            dyingStarted = true;
            StartCoroutine(DieRoutine());
        }
    }

    private IEnumerator DieRoutine()
    {
        // Matikan collider supaya bangkai tidak menghalangi pemain/tembakan berikutnya.
        foreach (Collider collider in GetComponentsInChildren<Collider>())
        {
            collider.enabled = false;
        }

        WildlifeManager.SpawnPickables(meatItemType, Mathf.Max(1, meatDropAmount), transform.position);

        yield return new WaitForSeconds(carcassSinkDelaySeconds);

        Vector3 startPosition = transform.position;
        float elapsed = 0f;
        while (elapsed < carcassSinkDuration)
        {
            elapsed += Time.deltaTime;
            transform.position = startPosition - Vector3.up * (elapsed * 0.35f);
            yield return null;
        }

        Destroy(gameObject);
    }
}
