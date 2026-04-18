using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class PlayerAxeCombat : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private Camera playerCamera;
    [SerializeField] private Transform axeRoot;
    [SerializeField] private Transform axeTip;
    [SerializeField] private Button attackButton;

    [Header("Axe Equip")]
    [SerializeField] private bool requireAxeEquipped = true;
    [SerializeField] private bool startAxeEquipped = false;
    [SerializeField] private bool autoDetectAxeRootByName = true;
    [SerializeField] private string axeNameContains = "axe";
    [SerializeField] private string axeTipNameContains = "tip";
    [SerializeField] private bool autoSpawnFallbackAxeIfMissing = true;
    [SerializeField] private Transform rightHandBone;
    [SerializeField] private GameObject fallbackAxePrefab;
    [SerializeField] private string fallbackAxeResourcesPath = "Prefabs/axe";
    [SerializeField] private Vector3 fallbackAxeLocalPosition = new Vector3(0.0165f, 0.1624f, 0.0275f);
    [SerializeField] private Vector3 fallbackAxeLocalEuler = new Vector3(84.63104f, 270.00067f, 90.00067f);
    [SerializeField] private Vector3 fallbackAxeLocalScale = Vector3.one;

    [Header("Attack Timing")]
    [SerializeField] private float attackCooldownSeconds = 0.6f;
    [SerializeField] private float hitDelaySeconds = 0.18f;

    [Header("Hit Detection")]
    [SerializeField] private float hitDistance = 1.4f;
    [SerializeField] private float hitRadius = 0.1f;
    [SerializeField] private LayerMask hittableLayers = ~0;
    [SerializeField] private bool debugDrawHitRay;

    [Header("Melee Assist")]
    [SerializeField] private bool enableTreeMeleeAssist = true;
    [SerializeField] private float fallbackCameraHitDistance = 2.4f;
    [SerializeField] private float treeMeleeAssistRadius = 1.9f;
    [SerializeField, Range(-1f, 1f)] private float treeMeleeAssistForwardDot = -0.2f;
    [SerializeField] private bool logHitDebug;

    [Header("Server Validation")]
    [SerializeField] private float serverTreeResolveRadius = 3.5f;
    [SerializeField] private float serverHitValidationDistance = 8f;
    [SerializeField] private float clientTreeSyncResolveRadius = 3.5f;
    [SerializeField] private bool logServerTreeHitDebug;

    [Header("Damage")]
    [SerializeField] private float treeDamagePerHit = 1f;
    [SerializeField] private float playerDamagePerHit = 10f;

    [Header("Animation")]
    [SerializeField] private string heavySwingStateName = "Standing Melee Attack Downward";
    [SerializeField] private string heavySwingTriggerName = "HeavyWeaponSwing";
    [SerializeField] private float swingCrossFadeSeconds = 0.08f;
    [SerializeField] private bool forceLocomotionExitFromSwing = true;
    [SerializeField, Range(0.4f, 1f)] private float swingExitNormalizedTime = 0.85f;
    [SerializeField] private float swingExitBlendSeconds = 0.1f;

    [Header("Locomotion Recovery")]
    [SerializeField] private string locomotionIdleStateName = "Idle";
    [SerializeField] private string locomotionWalkStateName = "Walk";
    [SerializeField] private string locomotionRunStateName = "Run";
    [SerializeField] private string locomotionFallStateName = "Fall";
    [SerializeField] private string speedParamName = "Speed";
    [SerializeField] private string isRunningParamName = "IsRunning";
    [SerializeField] private string isGroundedParamName = "IsGrounded";
    [SerializeField] private float walkSpeedThreshold = 0.1f;

    [Header("Attack Button")]
    [SerializeField] private bool autoBindAttackButton = true;
    [SerializeField] private bool autoCreateAttackButton = true;
    [SerializeField] private string attackButtonNameContains = "attack";
    [SerializeField] private string attackButtonLabel = "ATTACK";
    [SerializeField] private Vector2 attackButtonSize = new Vector2(150f, 90f);
    [SerializeField] private Vector2 attackButtonAnchorOffset = new Vector2(-260f, 120f);
    [SerializeField] private KeyCode keyboardAttackKey = KeyCode.F;
    [SerializeField] private bool allowKeyboardAttack = true;

    private bool axeEquipped;
    private bool attackButtonBound;
    private float nextAttackTime;
    private int heavySwingStateHash;
    private int heavySwingTriggerHash;
    private int locomotionIdleStateHash;
    private int locomotionWalkStateHash;
    private int locomotionRunStateHash;
    private int locomotionFallStateHash;
    private int speedParamHash;
    private int isRunningParamHash;
    private int isGroundedParamHash;
    private bool hasHeavySwingTriggerParam;
    private bool hasSpeedParam;
    private bool hasIsRunningParam;
    private bool hasIsGroundedParam;
    private Coroutine swingRecoveryRoutine;
    private int swingSequenceId;

    private void Awake()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        if (playerCamera == null)
        {
            playerCamera = GetComponentInChildren<Camera>(true);
        }

        this.TryAutoDetectAxeReferences();
        this.EnsureAxeVisualSetup();
        this.SetAxeEquipped(startAxeEquipped);
        this.CacheAnimationHashes();
    }

    private void OnEnable()
    {
        if (this.HasLocalAttackAuthority())
        {
            this.ResolveAttackButton();
            this.BindAttackButton();
            this.UpdateAttackButtonVisibility();
        }
    }

    private void OnDisable()
    {
        this.UnbindAttackButton();

        if (swingRecoveryRoutine != null)
        {
            StopCoroutine(swingRecoveryRoutine);
            swingRecoveryRoutine = null;
        }
    }

    private void Update()
    {
        if (!this.HasLocalAttackAuthority())
        {
            this.UpdateAttackButtonVisibility(false);
            return;
        }

        if (attackButton == null)
        {
            this.ResolveAttackButton();
        }

        this.BindAttackButton();

        this.UpdateAttackButtonVisibility(this.IsAxeEquipped());

        if (!this.IsAxeEquipped())
        {
            return;
        }

        if (allowKeyboardAttack && Input.GetKeyDown(keyboardAttackKey))
        {
            this.TryAttack();
        }
    }

    public void SetAxeEquipped(bool equipped)
    {
        axeEquipped = equipped;
        if (axeRoot != null && axeRoot.gameObject.activeSelf != equipped)
        {
            axeRoot.gameObject.SetActive(equipped);
        }
        this.UpdateAttackButtonVisibility();
    }

    public bool IsAxeEquipped()
    {
        if (!requireAxeEquipped)
        {
            return true;
        }

        if (axeRoot != null)
        {
            return axeRoot.gameObject.activeInHierarchy;
        }

        return axeEquipped;
    }

    public void TryAttack()
    {
        if (!this.HasLocalAttackAuthority())
        {
            return;
        }

        if (!this.IsAxeEquipped())
        {
            if (PickupUIManager.instance != null)
            {
                PickupUIManager.instance.ShowInfo("Kapak belum equipped.");
            }
            return;
        }

        if (Time.time < nextAttackTime)
        {
            return;
        }

        nextAttackTime = Time.time + Mathf.Max(0.05f, attackCooldownSeconds);
        this.PlaySwingAndScheduleRecovery();
        this.NotifySwingToRemoteClients();
        StartCoroutine(this.ResolveHitAfterDelay());
    }

    private IEnumerator ResolveHitAfterDelay()
    {
        float wait = Mathf.Max(0f, hitDelaySeconds);
        if (wait > 0f)
        {
            yield return new WaitForSeconds(wait);
        }

        if (!this.HasLocalAttackAuthority() || !this.IsAxeEquipped())
        {
            yield break;
        }

        if (this.TryGetHit(out RaycastHit hit))
        {
            this.ProcessHit(hit);
            yield break;
        }

        if (enableTreeMeleeAssist && this.TryGetNearestTreeAssist(out TreeChoppable assistedTree, out Vector3 assistPoint))
        {
            this.ApplyTreeHit(assistedTree, assistPoint);
            if (debugDrawHitRay)
            {
                Vector3 assistOrigin = this.GetHitOrigin();
                Debug.DrawLine(assistOrigin, assistPoint, Color.green, 0.35f);
            }
            yield break;
        }

        if (logHitDebug)
        {
            Debug.Log("PlayerAxeCombat: serangan tidak kena objek valid.");
        }
    }

    private void ProcessHit(RaycastHit hit)
    {
        if (hit.collider == null)
        {
            return;
        }

        TreeChoppable tree = hit.collider.GetComponentInParent<TreeChoppable>();
        if (tree != null)
        {
            this.ApplyTreeHit(tree, hit.point);
            return;
        }

        PlayerSurvivalSystem targetSurvival = hit.collider.GetComponentInParent<PlayerSurvivalSystem>();
        if (targetSurvival != null && targetSurvival.gameObject != gameObject)
        {
            NetworkObject targetNetworkObject = targetSurvival.GetComponent<NetworkObject>();
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned && IsOwner && targetNetworkObject != null)
            {
                this.RequestPlayerDamageServerRpc(targetNetworkObject.NetworkObjectId, playerDamagePerHit);
            }
            else
            {
                targetSurvival.ApplyDamage(playerDamagePerHit);
            }
        }
    }

    private void ApplyTreeHit(TreeChoppable tree, Vector3 hitPoint)
    {
        if (tree == null || tree.IsDepleted)
        {
            return;
        }

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned && IsOwner)
        {
            this.RequestTreeHitServerRpc(hitPoint, treeDamagePerHit);
            return;
        }

        tree.ApplyAxeHit(treeDamagePerHit, gameObject);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestPlayerDamageServerRpc(ulong targetNetworkObjectId, float damage, ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId || NetworkManager == null)
        {
            return;
        }

        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId, out NetworkObject targetObject))
        {
            return;
        }

        if (targetObject == null || targetObject == NetworkObject)
        {
            return;
        }

        float distance = Vector3.Distance(transform.position, targetObject.transform.position);
        if (distance > Mathf.Max(1.5f, serverHitValidationDistance))
        {
            return;
        }

        PlayerSurvivalSystem targetSurvival = targetObject.GetComponent<PlayerSurvivalSystem>();
        if (targetSurvival == null)
        {
            return;
        }

        targetSurvival.ApplyDamage(Mathf.Max(0f, damage));
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestTreeHitServerRpc(Vector3 hitPoint, float damage, ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
        {
            return;
        }

        TreeChoppable[] trees = FindObjectsOfType<TreeChoppable>(true);
        if (trees == null || trees.Length == 0)
        {
            return;
        }

        float resolveRadius = Mathf.Max(0.5f, serverTreeResolveRadius);
        float maxDistance = Mathf.Max(resolveRadius, serverHitValidationDistance);
        TreeChoppable bestTree = this.FindBestServerTreeCandidate(trees, hitPoint, transform.position, resolveRadius, maxDistance);

        if (bestTree != null)
        {
            float appliedDamage = Mathf.Max(0f, damage);
            bool accepted = bestTree.ApplyAxeHit(appliedDamage, gameObject);
            if (accepted)
            {
                this.SyncTreeHitClientRpc(bestTree.transform.position, appliedDamage, bestTree.IsDepleted);
            }
            else if (logServerTreeHitDebug)
            {
                Debug.Log($"Server tree hit ignored by debounce on '{bestTree.name}'.");
            }
        }
        else if (logServerTreeHitDebug)
        {
            Debug.Log(
                $"Server gagal resolve tree hit. Sender={rpcParams.Receive.SenderClientId}, " +
                $"hitPoint={hitPoint}, attacker={transform.position}, radius={resolveRadius}, maxDist={maxDistance}");
        }
    }

    [ClientRpc]
    private void SyncTreeHitClientRpc(Vector3 treePosition, float damage, bool depleted)
    {
        if (IsServer)
        {
            return;
        }

        if (this.TryFindTreeByPosition(treePosition, out TreeChoppable tree))
        {
            tree.ApplyReplicatedHit(damage, depleted);
        }
    }

    private TreeChoppable FindBestServerTreeCandidate(
        TreeChoppable[] trees,
        Vector3 hitPoint,
        Vector3 attackerPosition,
        float resolveRadius,
        float maxAttackerDistance)
    {
        TreeChoppable bestByHitPoint = null;
        TreeChoppable bestByAttackerDistance = null;
        float bestHitPointSqr = resolveRadius * resolveRadius;
        float bestAttackerSqr = float.MaxValue;
        float maxAttackerSqr = maxAttackerDistance * maxAttackerDistance;

        for (int i = 0; i < trees.Length; i++)
        {
            TreeChoppable candidate = trees[i];
            if (candidate == null || !candidate.gameObject.activeInHierarchy || candidate.IsDepleted)
            {
                continue;
            }

            float attackerSqr = (candidate.transform.position - attackerPosition).sqrMagnitude;
            if (attackerSqr > maxAttackerSqr)
            {
                continue;
            }

            float hitPointSqr = (candidate.transform.position - hitPoint).sqrMagnitude;
            if (hitPointSqr <= bestHitPointSqr)
            {
                bestHitPointSqr = hitPointSqr;
                bestByHitPoint = candidate;
            }

            if (attackerSqr < bestAttackerSqr)
            {
                bestAttackerSqr = attackerSqr;
                bestByAttackerDistance = candidate;
            }
        }

        return bestByHitPoint != null ? bestByHitPoint : bestByAttackerDistance;
    }

    private bool TryFindTreeByPosition(Vector3 treePosition, out TreeChoppable tree)
    {
        tree = null;
        TreeChoppable[] trees = FindObjectsOfType<TreeChoppable>(true);
        if (trees == null || trees.Length == 0)
        {
            return false;
        }

        float radius = Mathf.Max(0.5f, clientTreeSyncResolveRadius);
        float bestSqr = radius * radius;
        for (int i = 0; i < trees.Length; i++)
        {
            TreeChoppable candidate = trees[i];
            if (candidate == null || !candidate.gameObject.activeInHierarchy)
            {
                continue;
            }

            float sqr = (candidate.transform.position - treePosition).sqrMagnitude;
            if (sqr > bestSqr)
            {
                continue;
            }

            bestSqr = sqr;
            tree = candidate;
        }

        return tree != null;
    }

    private bool TryGetHit(out RaycastHit bestHit)
    {
        bestHit = default;
        Vector3 origin = this.GetHitOrigin();
        Vector3 direction = this.GetHitDirection();
        float distance = Mathf.Max(0.1f, hitDistance);
        float radius = Mathf.Max(0f, hitRadius);

        if (this.TryGetBestHitFromCast(origin, direction, distance, radius, out bestHit))
        {
            if (debugDrawHitRay)
            {
                Debug.DrawRay(origin, direction * distance, Color.yellow, 0.35f);
            }

            return true;
        }

        if (playerCamera != null)
        {
            Vector3 cameraOrigin = playerCamera.transform.position;
            Vector3 cameraDirection = playerCamera.transform.forward;
            float cameraDistance = Mathf.Max(distance, fallbackCameraHitDistance);
            if (this.TryGetBestHitFromCast(cameraOrigin, cameraDirection, cameraDistance, radius, out bestHit))
            {
                if (debugDrawHitRay)
                {
                    Debug.DrawRay(cameraOrigin, cameraDirection * cameraDistance, Color.cyan, 0.35f);
                }

                return true;
            }
        }

        if (debugDrawHitRay)
        {
            Debug.DrawRay(origin, direction * distance, Color.red, 0.35f);
        }

        return false;
    }

    private bool TryGetBestHitFromCast(
        Vector3 origin,
        Vector3 direction,
        float distance,
        float radius,
        out RaycastHit bestHit)
    {
        bestHit = default;

        RaycastHit[] hits;
        if (radius <= 0.0001f)
        {
            hits = Physics.RaycastAll(origin, direction, distance, hittableLayers, QueryTriggerInteraction.Ignore);
        }
        else
        {
            hits = Physics.SphereCastAll(origin, radius, direction, distance, hittableLayers, QueryTriggerInteraction.Ignore);
        }

        if (hits == null || hits.Length == 0)
        {
            return false;
        }

        float nearestDistance = float.MaxValue;
        bool found = false;

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null)
            {
                continue;
            }

            if (hit.collider.transform.root == transform.root)
            {
                continue;
            }

            if (hit.distance < nearestDistance)
            {
                nearestDistance = hit.distance;
                bestHit = hit;
                found = true;
            }
        }

        return found;
    }

    private bool TryGetNearestTreeAssist(out TreeChoppable nearestTree, out Vector3 hitPoint)
    {
        nearestTree = null;
        hitPoint = default;

        TreeChoppable[] trees = FindObjectsOfType<TreeChoppable>(true);
        if (trees == null || trees.Length == 0)
        {
            return false;
        }

        Vector3 origin = this.GetHitOrigin();
        Vector3 forward = playerCamera != null ? playerCamera.transform.forward : transform.forward;
        float bestSqr = Mathf.Max(0.25f, treeMeleeAssistRadius) * Mathf.Max(0.25f, treeMeleeAssistRadius);

        for (int i = 0; i < trees.Length; i++)
        {
            TreeChoppable tree = trees[i];
            if (tree == null || tree.IsDepleted || !tree.gameObject.activeInHierarchy)
            {
                continue;
            }

            Collider treeCollider = tree.GetComponentInChildren<Collider>();
            if (treeCollider == null)
            {
                continue;
            }

            Vector3 candidatePoint = treeCollider.ClosestPoint(origin);
            Vector3 toTree = candidatePoint - origin;
            float sqr = toTree.sqrMagnitude;
            if (sqr > bestSqr)
            {
                continue;
            }

            Vector3 dirNorm = toTree.sqrMagnitude > 0.0001f ? toTree.normalized : forward;
            float dot = Vector3.Dot(forward, dirNorm);
            if (dot < treeMeleeAssistForwardDot)
            {
                continue;
            }

            if (Physics.Linecast(origin, candidatePoint, out RaycastHit blockHit, hittableLayers, QueryTriggerInteraction.Ignore))
            {
                if (blockHit.collider != null && blockHit.collider.transform.root != tree.transform.root)
                {
                    continue;
                }
            }

            bestSqr = sqr;
            nearestTree = tree;
            hitPoint = candidatePoint;
        }

        return nearestTree != null;
    }

    private Vector3 GetHitOrigin()
    {
        if (axeTip != null)
        {
            return axeTip.position;
        }

        if (axeRoot != null)
        {
            return axeRoot.position;
        }

        if (playerCamera != null)
        {
            return playerCamera.transform.position;
        }

        return transform.position + (Vector3.up * 1.2f);
    }

    private Vector3 GetHitDirection()
    {
        if (axeTip != null)
        {
            return axeTip.forward;
        }

        if (axeRoot != null)
        {
            return axeRoot.forward;
        }

        if (playerCamera != null)
        {
            return playerCamera.transform.forward;
        }

        return transform.forward;
    }

    private void PlaySwingAnimation()
    {
        if (animator == null)
        {
            return;
        }

        if (hasHeavySwingTriggerParam)
        {
            animator.SetTrigger(heavySwingTriggerHash);
            return;
        }

        if (!string.IsNullOrWhiteSpace(heavySwingStateName) && animator.HasState(0, heavySwingStateHash))
        {
            animator.CrossFade(heavySwingStateHash, Mathf.Max(0.01f, swingCrossFadeSeconds), 0, 0f);
            return;
        }

        Debug.LogWarning(
            "PlayerAxeCombat: state/trigger animasi heavy swing belum ditemukan. " +
            "Tambahkan state '" + heavySwingStateName + "' atau trigger '" + heavySwingTriggerName + "' di Animator.");
    }

    private void PlaySwingAndScheduleRecovery()
    {
        this.PlaySwingAnimation();

        if (!forceLocomotionExitFromSwing || animator == null)
        {
            return;
        }

        swingSequenceId++;
        if (swingRecoveryRoutine != null)
        {
            StopCoroutine(swingRecoveryRoutine);
        }

        swingRecoveryRoutine = StartCoroutine(this.RecoverLocomotionAfterSwing(swingSequenceId));
    }

    private IEnumerator RecoverLocomotionAfterSwing(int sequenceId)
    {
        float waitSeconds = this.GetSwingRecoveryDelay();
        if (waitSeconds > 0f)
        {
            yield return new WaitForSeconds(waitSeconds);
        }

        if (sequenceId != swingSequenceId || animator == null)
        {
            yield break;
        }

        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
        if (stateInfo.shortNameHash != heavySwingStateHash)
        {
            yield break;
        }

        int targetState = this.ResolveLocomotionRecoveryState();
        if (targetState == 0 || !animator.HasState(0, targetState))
        {
            yield break;
        }

        animator.CrossFade(targetState, Mathf.Max(0.02f, swingExitBlendSeconds), 0, 0f);
    }

    private float GetSwingRecoveryDelay()
    {
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;
            for (int i = 0; i < clips.Length; i++)
            {
                AnimationClip clip = clips[i];
                if (clip == null)
                {
                    continue;
                }

                if (clip.name == heavySwingStateName || clip.name.Contains(heavySwingStateName))
                {
                    return Mathf.Max(0.05f, clip.length * swingExitNormalizedTime);
                }
            }
        }

        return Mathf.Max(0.08f, attackCooldownSeconds * swingExitNormalizedTime);
    }

    private int ResolveLocomotionRecoveryState()
    {
        bool grounded = hasIsGroundedParam ? animator.GetBool(isGroundedParamHash) : true;
        bool running = hasIsRunningParam && animator.GetBool(isRunningParamHash);
        float speed = hasSpeedParam ? animator.GetFloat(speedParamHash) : 0f;

        if (!grounded && animator.HasState(0, locomotionFallStateHash))
        {
            return locomotionFallStateHash;
        }

        if (speed > walkSpeedThreshold)
        {
            if (running && animator.HasState(0, locomotionRunStateHash))
            {
                return locomotionRunStateHash;
            }

            if (animator.HasState(0, locomotionWalkStateHash))
            {
                return locomotionWalkStateHash;
            }
        }

        return locomotionIdleStateHash;
    }

    private void NotifySwingToRemoteClients()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || !IsSpawned || !IsOwner)
        {
            return;
        }

        this.BroadcastSwingServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void BroadcastSwingServerRpc(ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
        {
            return;
        }

        this.PlaySwingRemoteClientRpc();
    }

    [ClientRpc]
    private void PlaySwingRemoteClientRpc()
    {
        if (IsOwner)
        {
            return;
        }

        this.PlaySwingAndScheduleRecovery();
    }

    private void CacheAnimationHashes()
    {
        heavySwingStateHash = Animator.StringToHash(heavySwingStateName);
        heavySwingTriggerHash = Animator.StringToHash(heavySwingTriggerName);
        locomotionIdleStateHash = Animator.StringToHash(locomotionIdleStateName);
        locomotionWalkStateHash = Animator.StringToHash(locomotionWalkStateName);
        locomotionRunStateHash = Animator.StringToHash(locomotionRunStateName);
        locomotionFallStateHash = Animator.StringToHash(locomotionFallStateName);
        speedParamHash = Animator.StringToHash(speedParamName);
        isRunningParamHash = Animator.StringToHash(isRunningParamName);
        isGroundedParamHash = Animator.StringToHash(isGroundedParamName);
        hasHeavySwingTriggerParam = false;
        hasSpeedParam = false;
        hasIsRunningParam = false;
        hasIsGroundedParam = false;

        if (animator == null)
        {
            return;
        }

        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter parameter = parameters[i];
            if (parameter.type == AnimatorControllerParameterType.Trigger && parameter.nameHash == heavySwingTriggerHash)
            {
                hasHeavySwingTriggerParam = true;
            }

            if (parameter.type == AnimatorControllerParameterType.Float && parameter.nameHash == speedParamHash)
            {
                hasSpeedParam = true;
            }

            if (parameter.type == AnimatorControllerParameterType.Bool && parameter.nameHash == isRunningParamHash)
            {
                hasIsRunningParam = true;
            }

            if (parameter.type == AnimatorControllerParameterType.Bool && parameter.nameHash == isGroundedParamHash)
            {
                hasIsGroundedParam = true;
            }
        }
    }

    private bool HasLocalAttackAuthority()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            return true;
        }

        if (!IsSpawned)
        {
            return false;
        }

        return IsOwner;
    }

    private void ResolveAttackButton()
    {
        if (attackButton == null && autoBindAttackButton)
        {
            attackButton = this.FindAttackButton();
        }

        if (attackButton == null && autoCreateAttackButton)
        {
            attackButton = this.CreateAttackButton();
        }
    }

    private void BindAttackButton()
    {
        if (attackButton == null || attackButtonBound)
        {
            return;
        }

        attackButton.onClick.AddListener(this.TryAttack);
        attackButtonBound = true;
    }

    private void UnbindAttackButton()
    {
        if (attackButton == null || !attackButtonBound)
        {
            return;
        }

        attackButton.onClick.RemoveListener(this.TryAttack);
        attackButtonBound = false;
    }

    private void UpdateAttackButtonVisibility()
    {
        this.UpdateAttackButtonVisibility(this.HasLocalAttackAuthority() && this.IsAxeEquipped());
    }

    private void UpdateAttackButtonVisibility(bool visible)
    {
        if (attackButton != null)
        {
            attackButton.gameObject.SetActive(visible);
        }
    }

    private Button FindAttackButton()
    {
        Button[] buttons = FindObjectsOfType<Button>(true);
        string keyword = string.IsNullOrWhiteSpace(attackButtonNameContains)
            ? "attack"
            : attackButtonNameContains.ToLowerInvariant();

        for (int i = 0; i < buttons.Length; i++)
        {
            Button button = buttons[i];
            if (button == null)
            {
                continue;
            }

            if (button.name.ToLowerInvariant().Contains(keyword))
            {
                return button;
            }
        }

        return null;
    }

    private Button CreateAttackButton()
    {
        Canvas canvas = this.FindCanvasForGameplayUI();
        if (canvas == null)
        {
            return null;
        }

        RectTransform parent = canvas.transform as RectTransform;
        Transform movementContainer = this.FindDeepChildByName(canvas.transform, "Container Movement");
        if (movementContainer != null)
        {
            parent = movementContainer as RectTransform;
        }

        if (parent == null)
        {
            return null;
        }

        GameObject buttonObject = new GameObject(
            "Attack Button",
            typeof(RectTransform),
            typeof(Image),
            typeof(Button));

        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0f);
        rect.sizeDelta = attackButtonSize;
        rect.anchoredPosition = attackButtonAnchorOffset;

        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color(0.45f, 0.16f, 0.16f, 0.92f);

        Button button = buttonObject.GetComponent<Button>();

        GameObject textObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.SetParent(rect, false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(6f, 6f);
        textRect.offsetMax = new Vector2(-6f, -6f);

        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.text = attackButtonLabel;
        text.alignment = TextAlignmentOptions.Center;
        text.fontSize = 30f;
        text.enableAutoSizing = true;
        text.color = Color.white;
        text.fontStyle = FontStyles.Bold;

        return button;
    }

    private Canvas FindCanvasForGameplayUI()
    {
        Canvas[] canvases = FindObjectsOfType<Canvas>(true);
        if (canvases == null || canvases.Length == 0)
        {
            return null;
        }

        Canvas fallback = canvases[0];
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas canvas = canvases[i];
            if (canvas == null)
            {
                continue;
            }

            if (canvas.GetComponent<PlayerSurvivalUI>() != null || canvas.GetComponent<PickupUIManager>() != null)
            {
                return canvas;
            }
        }

        return fallback;
    }

    private void TryAutoDetectAxeReferences()
    {
        if (!autoDetectAxeRootByName)
        {
            return;
        }

        if (axeRoot == null)
        {
            Transform foundAxe = this.FindDeepChildContains(transform, axeNameContains);
            if (foundAxe != null)
            {
                axeRoot = foundAxe;
            }
        }

        if (axeTip == null && axeRoot != null)
        {
            Transform foundTip = this.FindDeepChildContains(axeRoot, axeTipNameContains);
            axeTip = foundTip != null ? foundTip : axeRoot;
        }
    }

    private void EnsureAxeVisualSetup()
    {
        this.TryAutoDetectAxeReferences();
        if (axeRoot != null)
        {
            if (axeTip == null)
            {
                Transform foundTip = this.FindDeepChildContains(axeRoot, axeTipNameContains);
                axeTip = foundTip != null ? foundTip : axeRoot;
            }
            return;
        }

        if (!autoSpawnFallbackAxeIfMissing)
        {
            return;
        }

        Transform hand = this.ResolveRightHandBone();
        if (hand == null)
        {
            return;
        }

        Transform existing = this.FindDeepChildContains(hand, axeNameContains);
        if (existing != null)
        {
            axeRoot = existing;
            axeTip = this.FindDeepChildContains(existing, axeTipNameContains);
            if (axeTip == null)
            {
                axeTip = existing;
            }
            return;
        }

        GameObject sourcePrefab = fallbackAxePrefab;
        if (sourcePrefab == null && !string.IsNullOrWhiteSpace(fallbackAxeResourcesPath))
        {
            sourcePrefab = Resources.Load<GameObject>(fallbackAxeResourcesPath);
        }

        if (sourcePrefab == null)
        {
            return;
        }

        GameObject spawned = Instantiate(sourcePrefab, hand);
        spawned.name = "axe";
        spawned.transform.localPosition = fallbackAxeLocalPosition;
        spawned.transform.localRotation = Quaternion.Euler(fallbackAxeLocalEuler);
        spawned.transform.localScale = fallbackAxeLocalScale;

        axeRoot = spawned.transform;
        Transform spawnedTip = this.FindDeepChildContains(axeRoot, axeTipNameContains);
        axeTip = spawnedTip != null ? spawnedTip : axeRoot;
    }

    private Transform ResolveRightHandBone()
    {
        if (rightHandBone != null)
        {
            return rightHandBone;
        }

        if (animator != null && animator.isHuman)
        {
            rightHandBone = animator.GetBoneTransform(HumanBodyBones.RightHand);
        }

        if (rightHandBone == null)
        {
            rightHandBone = this.FindDeepChildContains(transform, "RightHand");
        }

        return rightHandBone;
    }

    private Transform FindDeepChildContains(Transform root, string keyword)
    {
        if (root == null || string.IsNullOrWhiteSpace(keyword))
        {
            return null;
        }

        string lowered = keyword.ToLowerInvariant();
        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child == null)
            {
                continue;
            }

            if (child.name.ToLowerInvariant().Contains(lowered))
            {
                return child;
            }

            Transform nested = this.FindDeepChildContains(child, lowered);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }

    private Transform FindDeepChildByName(Transform root, string objectName)
    {
        if (root == null || string.IsNullOrWhiteSpace(objectName))
        {
            return null;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child == null)
            {
                continue;
            }

            if (child.name == objectName)
            {
                return child;
            }

            Transform nested = this.FindDeepChildByName(child, objectName);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }
}
