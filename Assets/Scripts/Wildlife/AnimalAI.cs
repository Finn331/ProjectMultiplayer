using System.Collections;
using Fusion;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// AI hewan arktik NETWORKED (v2): state authority = master client menjalankan seluruh
/// logika (Idle/Wander/Flee/Chase/Attack) + NavMeshAgent; klien lain hanya lerp ke
/// posisi/rotasi tersinkronisasi. HP tersinkron via [Networked]; kapak dari klien mana pun
/// masuk lewat RPC (pola sama dengan tebang pohon). Mati -> semua klien spawn drop daging
/// lokal + bangkai tenggelam; authority despawn object. Predator melukai player via RPC ke
/// pemilik karakter (shared mode: tiap pemain state authority atas karakternya sendiri).
/// </summary>
public class AnimalAI : NetworkBehaviour
{
    public enum State { Idle, Wander, Flee, Chase, Attack, Dead }

    [Header("Identitas")]
    public string speciesName = "Deer";

    [Header("Stat")]
    [SerializeField] private float maxHealth = 60f;
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

    [Networked] public float Health { get; set; }
    [Networked] public NetworkBool IsDead { get; set; }
    [Networked] private Vector3 SyncPosition { get; set; }
    [Networked] private Quaternion SyncRotation { get; set; }

    public State CurrentState { get; private set; } = State.Idle;

    private NavMeshAgent agent;
    private Transform playerTarget;
    private Vector3 homePosition;
    private Vector3 wanderDestination;
    private float wanderTimer;
    private float detectTimer;
    private float attackCooldown;
    private bool deathVisualApplied;
    private bool authorityDespawnScheduled;
    // Networked properties throw InvalidOperationException before Spawned() runs; Update()
    // ticks from the first frame, so every networked read in Update must wait for this flag.
    private bool _spawned;

    public void InitializeFromConfig(bool predatorFlag, float healthMax, float speedWalk, float speedRun,
        float aggro, float flee, float damage, int meatCount)
    {
        // Dipanggil WildlifeMaster saat prefab tidak carry konfigurasi (jalur prosedural).
        isPredator = predatorFlag;
        maxHealth = healthMax;
        walkSpeed = speedWalk;
        runSpeed = speedRun;
        aggroRadius = aggro;
        fleeRadius = flee;
        attackDamage = damage;
        meatDropAmount = meatCount;
    }

    public override void Spawned()
    {
        _spawned = true;
        agent = GetComponent<NavMeshAgent>();
        CurrentState = State.Idle;
        homePosition = transform.position;
        wanderDestination = transform.position;
        wanderTimer = Random.Range(0.5f, 3f);

        if (Object.HasStateAuthority)
        {
            Health = maxHealth;
            if (agent != null)
            {
                // Guard anti-drift-Y: kalau posisi spawn belum di NavMesh, cari
                // titik valid terdekat dan Warp SEBELUM agent di-enable.
                if (!agent.isOnNavMesh
                    && UnityEngine.AI.NavMesh.SamplePosition(transform.position, out NavMeshHit spawnHit, 8f, UnityEngine.AI.NavMesh.AllAreas))
                {
                    transform.position = spawnHit.position;
                    agent.Warp(spawnHit.position);
                }
                agent.enabled = true;
            }
        }
        else
        {
            if (agent != null)
            {
                // Safety net: prefab lama / replika diterima sebelum bake NavMesh
                // lokal bisa menyisakan agent AKTIF tanpa NavMesh (agent rusak,
                // tidak auto-recover). Proxy tidak memakai agent, jadi matikan.
                agent.enabled = false;
            }
            SyncPosition = transform.position;
            SyncRotation = transform.rotation;
        }
    }

    private void Update()
    {
        if (!_spawned)
        {
            return;
        }

        if (IsDead)
        {
            if (!deathVisualApplied)
            {
                ApplyDeathVisuals();
            }

            return;
        }

        if (!Object.HasStateAuthority)
        {
            // Proxy: haluskan menuju posisi tersinkronisasi.
            float t = Time.deltaTime * 8f;
            transform.position = Vector3.Lerp(transform.position, SyncPosition, t);
            transform.rotation = Quaternion.Slerp(transform.rotation, SyncRotation, t);
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority || IsDead)
        {
            return;
        }

        if (Health <= 0f)
        {
            BeginDeath();
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
                MoveToward(wanderDestination, walkSpeed);
                wanderTimer -= Time.deltaTime;
                if (HasArrived(wanderDestination) || wanderTimer <= 0f)
                {
                    CurrentState = State.Idle;
                    wanderTimer = Random.Range(1.5f, 4f);
                }
                break;

            case State.Flee:
                if (playerTarget == null)
                {
                    CurrentState = State.Wander;
                    break;
                }

                Vector3 away = transform.position - playerTarget.position;
                away.y = 0f;
                MoveToward(transform.position + away.normalized * 6f, runSpeed);
                break;

            case State.Chase:
                if (playerTarget == null)
                {
                    CurrentState = State.Wander;
                    break;
                }

                MoveToward(playerTarget.position, runSpeed);
                if (GetFlatDistance(playerTarget.position) <= attackRange)
                {
                    CurrentState = State.Attack;
                    StopMovement();
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
                    NetworkObject victimObject = playerTarget.GetComponentInParent<FusionPlayerSurvival>() != null
                        ? playerTarget.GetComponentInParent<FusionPlayerSurvival>().Object
                        : null;
                    if (victimObject != null)
                    {
                        RPC_DamagePlayer(victimObject.InputAuthority, attackDamage);
                    }
                }
                break;
        }

        PublishSync();
    }

    /// <summary>Dipanggil PlayerAxeCombat di klien mana pun; route otomatis ke authority.</summary>
    public void TakeDamage(float amount)
    {
        if (IsDead || amount <= 0f)
        {
            return;
        }

        if (Object == null || !Object.IsValid)
        {
            // Jalur non-networked (uji editor / tanpa sesi): HP lokal saja.
            Health = Mathf.Max(0f, Health - amount);
            return;
        }

        if (Object.HasStateAuthority)
        {
            ApplyHitLocally(amount);
        }
        else
        {
            RPC_RequestHit(amount);
        }
    }

    [Rpc(RpcSources.All, RpcTargets.All)]
    private void RPC_RequestHit(float amount, RpcInfo info = default)
    {
        if (Object.HasStateAuthority)
        {
            ApplyHitLocally(amount);
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_DamagePlayer(PlayerRef victimPlayer, float amount, RpcInfo info = default)
    {
        if (Runner == null || Runner.LocalPlayer != victimPlayer)
        {
            return;
        }

        foreach (var networkObject in FindObjectsOfType<Fusion.NetworkObject>())
        {
            if (!networkObject.HasStateAuthority || networkObject.InputAuthority != Runner.LocalPlayer)
            {
                continue;
            }

            var survival = networkObject.GetComponent<FusionPlayerSurvival>();
            if (survival != null)
            {
                survival.ApplyDamageForStateAuthority(amount, Fusion.PlayerRef.None);
            }

            break;
        }
    }

    private void ApplyHitLocally(float amount)
    {
        Health = Mathf.Max(0f, Health - amount);

        UpdatePlayerTarget();
        if (playerTarget == null)
        {
            return;
        }

        if (!isPredator)
        {
            CurrentState = State.Flee;
        }
        else if (CurrentState != State.Attack)
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

    private void MoveToward(Vector3 destination, float speed)
    {
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.speed = speed;
            agent.SetDestination(destination);
            return;
        }

        StepTowardRaycast(destination, speed);
    }

    private void StopMovement()
    {
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.ResetPath();
        }
    }

    private void StepTowardRaycast(Vector3 destination, float speed)
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

    private void PublishSync()
    {
        SyncPosition = transform.position;
        SyncRotation = transform.rotation;
    }

    private void BeginDeath()
    {
        IsDead = true;
    }

    private void ApplyDeathVisuals()
    {
        deathVisualApplied = true;
        CurrentState = State.Dead;

        if (agent != null)
        {
            agent.enabled = false;
        }

        foreach (Collider collider in GetComponentsInChildren<Collider>())
        {
            collider.enabled = false;
        }

        // Drop daging LOKAL di tiap klien (siapa pun yang mengambil, masuk inventarisnya).
        WildlifeManager.SpawnLocalMeatCubes(meatItemType, Mathf.Max(1, meatDropAmount), transform.position);

        StartCoroutine(SinkRoutine());
        if (Object.HasStateAuthority && !authorityDespawnScheduled)
        {
            authorityDespawnScheduled = true;
            StartCoroutine(AuthorityDespawnRoutine());
        }
    }

    private IEnumerator SinkRoutine()
    {
        yield return new WaitForSeconds(carcassSinkDelaySeconds);

        Vector3 startPosition = transform.position;
        float elapsed = 0f;
        while (elapsed < carcassSinkDuration && this != null && gameObject != null)
        {
            elapsed += Time.deltaTime;
            transform.position = startPosition - Vector3.up * (elapsed * 0.35f);
            yield return null;
        }
    }

    private IEnumerator AuthorityDespawnRoutine()
    {
        yield return new WaitForSeconds(carcassSinkDelaySeconds + carcassSinkDuration + 0.5f);
        if (Object != null && Object.IsValid && Runner != null)
        {
            Runner.Despawn(Object);
        }
    }
}
