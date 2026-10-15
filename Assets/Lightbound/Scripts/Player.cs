using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Player controller: movement, jumping, throwing/recalling light, shadow wall phasing, and checkpoints.
/// Works alongside PhotonicMass, LevelObstacles, and GameHUD.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class Player : MonoBehaviour
{
    /// <summary>Inner radius as a fraction of the outer one. Low = soft glow that fades out, ~1 = hard circle.</summary>
    public const float GlowInner = 0.3f;

    [Header("Movement")]
    public float moveSpeed = 7f;
    public float jumpVelocity = 13f;
    public LayerMask groundMask;

    [Header("Photonic Mass (Body Made of Light Twist)")]
    public float minBodyScale = 0.42f;
    public float maxBodyScale = 1.18f;
    public float tinyMoveSpeed = 9.0f;
    public float heavyMoveSpeed = 6.8f;
    public float tinyGravityScale = 1.7f;
    public float heavyGravityScale = 2.95f;
    public float heavySlamThreshold = 65f;
    public float slamVelocity = -22f;
    public float slamShockwaveRadius = 3.8f;
    public float recallRange = 18f;

    [Header("Light")]
    public Transform lightMask;       // child with a soft Point Light 2D
    public float maxLight = 100f;
    public float lightAmount = 100f;
    public float throwCost = 15f;
    public float maxRadius = 4.5f;
    public float minRadius = 1.6f;
    public float darkRadius = 0.8f;   // small glow left when the light is used up
    [Tooltip("How far the light shifts ahead in the direction you're moving, as a fraction of its radius.")]
    [Range(0f, 0.8f)] public float lightLead = 0.35f;

    [Header("Throwing")]
    [Tooltip("Disabled LightOrb object in the scene; each throw clones it.")]
    public Rigidbody2D orbTemplate;
    public float throwSpeed = 15f;

    [Header("Aim")]
    [Tooltip("Dotted line showing where a thrown orb will fly (it follows the orb's gravity arc).")]
    public bool showAim = true;
    public float aimLength = 9f;
    public float aimSpacing = 0.55f;

    [Header("Shadow walls")]
    [Tooltip("Layer of the shadow walls (the barriers, tag Barrier). Thrown light breaks them; with 0 light you slip through them.")]
    public LayerMask shadowMask;

    [Header("Look")]
    [Tooltip("Dark visor (the player's eyes); flips to the side you're facing.")]
    public Transform visor;

    [Header("Checkpoints")]
    [Tooltip("How many thrown light orbs a checkpoint has to absorb before it lights up.")]
    public int checkpointCost = 2;
    public float checkpointMinLight = 45f;
    public float respawnInvulnerability = 1.2f;

    public bool HasLight => lightAmount > 0f;
    public bool HasWon => _won;
    public int FacingDirection => _facing;
    public bool IsHeavy => _mass != null ? _mass.IsHeavy : lightAmount >= heavySlamThreshold;
    public bool IsTiny => _mass != null && _mass.IsTiny;
    public float CurrentBodyScale => _mass != null ? _mass.CurrentScale : 1f;
    public float LightRadius => _light != null ? _light.pointLightOuterRadius : 0f;
    /// <summary>Centre of the light circle (it leads ahead of the player). Enemies notice you from here.</summary>
    public Vector2 LightCenter => lightMask != null ? (Vector2)lightMask.position : (Vector2)transform.position;

    static string _cpScene;
    static Vector2? _cpPos;
    static float _cpLight;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { _cpScene = null; _cpPos = null; }

    Rigidbody2D _rb;
    Light2D _light;
    BoxCollider2D _col;
    SpriteRenderer[] _sprites;
    PhotonicMass _mass;
    LevelObstacles _obstacles;
    GameHUD _hud;

    bool _grounded;
    bool _won;
    Collider2D[] _shadowCols = new Collider2D[0];
    SpriteRenderer[] _shadowSprites = new SpriteRenderer[0];
    bool _phased;
    int _facing = 1;
    float _invulnUntil;
    readonly Dictionary<GameObject, int> _charge = new Dictionary<GameObject, int>();

    public static Player Instance { get; private set; }

    void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _light = lightMask.GetComponent<Light2D>();
        _col = GetComponent<BoxCollider2D>();

        _mass = GetComponent<PhotonicMass>();
        if (_mass == null) _mass = gameObject.AddComponent<PhotonicMass>();
        _mass.minBodyScale = minBodyScale;
        _mass.maxBodyScale = maxBodyScale;
        _mass.tinyMoveSpeed = tinyMoveSpeed;
        _mass.heavyMoveSpeed = heavyMoveSpeed;
        _mass.tinyGravityScale = tinyGravityScale;
        _mass.heavyGravityScale = heavyGravityScale;
        _mass.heavySlamThreshold = heavySlamThreshold;
        _mass.slamVelocity = slamVelocity;
        _mass.slamShockwaveRadius = slamShockwaveRadius;

        _obstacles = GetComponent<LevelObstacles>();
        if (_obstacles == null) _obstacles = gameObject.AddComponent<LevelObstacles>();
        _obstacles.SetupLevelObstacles();

        _hud = GetComponent<GameHUD>();
        if (_hud == null) _hud = gameObject.AddComponent<GameHUD>();

        _sprites = GetComponentsInChildren<SpriteRenderer>();
        var walls = GameObject.FindGameObjectsWithTag("Barrier");
        _shadowCols = new Collider2D[walls.Length];
        _shadowSprites = new SpriteRenderer[walls.Length];
        for (int i = 0; i < walls.Length; i++)
        {
            _shadowCols[i] = walls[i].GetComponent<Collider2D>();
            _shadowSprites[i] = walls[i].GetComponent<SpriteRenderer>();
        }
        Instance = this;

        if (_cpPos.HasValue && _cpScene == SceneManager.GetActiveScene().name)
        {
            transform.position = _cpPos.Value + Vector2.up * 0.6f;
            lightAmount = _cpLight;
            _invulnUntil = Time.time + respawnInvulnerability;
            foreach (var cp in GameObject.FindGameObjectsWithTag("Checkpoint"))
                if (Vector2.Distance(cp.transform.position, _cpPos.Value) < 0.01f) LightUp(cp, 1f);
            ShowMessage("Back at the checkpoint");
        }
        else
        {
            _cpPos = null;
            _cpScene = null;
            GameHUD.ResetRunStats();
        }

        _mass.Initialize(this, _rb, _col);
    }

    void FixedUpdate()
    {
        if (_won || _mass == null) return;
        int standOn = _phased ? (int)groundMask : (groundMask | shadowMask);
        float lightRatio = Mathf.Clamp01(lightAmount / maxLight);
        _mass.FixedUpdateScale(lightRatio, standOn, _grounded);
    }

    void Update()
    {
        if (_obstacles != null) _obstacles.TickRespawningShards();

        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (kb != null && kb.rKey.wasPressedThisFrame)
        {
            if (_won || kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed) RestartFromBeginning();
            else Respawn();
        }
        if (_won)
        {
            if (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame))
                RestartFromBeginning();
            return;
        }
        if (kb == null) return;

        int standOn = _phased ? (int)groundMask : (groundMask | shadowMask);
        float lightRatio = Mathf.Clamp01(lightAmount / maxLight);

        _rb.gravityScale = _mass.GetGravityScale(lightRatio);

        float x = (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? 1f : 0f);
        Vector2 v = _rb.linearVelocity;
        v.x = x * _mass.GetMoveSpeed(lightRatio);

        _grounded = Physics2D.OverlapBox(
            (Vector2)_col.bounds.center + Vector2.down * _col.bounds.extents.y,
            new Vector2(_col.bounds.size.x * 0.88f, 0.14f),
            0f,
            standOn);

        if (_grounded && _mass.IsSlamming)
        {
            _mass.IsSlamming = false;
            _mass.ExecuteSlamShockwave(new Vector2(transform.position.x, _col.bounds.min.y), _obstacles != null ? _obstacles.CrackedBlocks : null);
        }

        if (_grounded && (kb.spaceKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame))
        {
            v.y = jumpVelocity * _mass.GetJumpMultiplier(lightRatio);
            _mass.IsSlamming = false;
        }
        if ((kb.spaceKey.wasReleasedThisFrame || kb.wKey.wasReleasedThisFrame || kb.upArrowKey.wasReleasedThisFrame) && v.y > 0f)
            v.y *= 0.5f;

        if (!_grounded && (kb.sKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame))
        {
            if (IsHeavy)
            {
                _mass.IsSlamming = true;
                v.y = slamVelocity;
            }
            else
            {
                ShowMessage($"Need {Mathf.CeilToInt(heavySlamThreshold)}+ Light (Heavy Form) to Heavy Slam! Recall orbs (Right-Click) or find a Shard.");
            }
        }

        _rb.linearVelocity = v;

        if (mouse != null && mouse.leftButton.wasPressedThisFrame && HasLight)
        {
            Vector2 aim = ((Vector2)Camera.main.ScreenToWorldPoint(mouse.position.ReadValue()) - (Vector2)transform.position).normalized;
            float spawnDist = 0.45f * CurrentBodyScale + 0.25f;
            var orb = Instantiate(orbTemplate, transform.position + (Vector3)(aim * spawnDist), Quaternion.identity);
            orb.gameObject.SetActive(true);
            Physics2D.IgnoreCollision(orb.GetComponent<Collider2D>(), _col);
            orb.linearVelocity = aim * throwSpeed;
            lightAmount = Mathf.Max(0f, lightAmount - throwCost);
            GameHUD.RecordOrbThrown();
        }

        if ((mouse != null && mouse.rightButton.wasPressedThisFrame) || kb.eKey.wasPressedThisFrame)
        {
            RecallNearbyOrbs();
        }

        if (_obstacles != null) _obstacles.CheckHints(this);

        if (x != 0f) _facing = x > 0f ? 1 : -1;
        _mass.ApplyVisualScale(_facing);

        float target = HasLight ? Mathf.Lerp(minRadius, maxRadius, lightRatio) : darkRadius;
        float r = Mathf.Lerp(LightRadius, target, Time.deltaTime * 6f);
        _light.pointLightOuterRadius = r;
        _light.pointLightInnerRadius = r * GlowInner;

        UpdateShadowWalls();
        Vector3 lead = new Vector3(_facing * r * lightLead, 0f, 0f);
        lightMask.localPosition = Vector3.Lerp(lightMask.localPosition, lead, Time.deltaTime * 5f);

        bool blink = Time.time < _invulnUntil && Mathf.Repeat(Time.time * 10f, 1f) < 0.5f;
        foreach (var s in _sprites) if (s != null) s.enabled = !blink;

        if (transform.position.y < -4f) Respawn();
    }

    void RecallNearbyOrbs()
    {
        int count = 0;
        for (int i = LightOrb.ActiveOrbs.Count - 1; i >= 0; i--)
        {
            var orb = LightOrb.ActiveOrbs[i];
            if (orb == null || orb.IsRecalling) continue;
            if (Vector2.Distance(transform.position, orb.transform.position) <= recallRange)
            {
                float refund = throwCost * Mathf.Lerp(0.75f, 1f, orb.RemainingFraction);
                orb.Recall(refund);
                count++;
            }
        }
        if (count > 0) ShowMessage($"Recalling {count} light orb(s)");
        else ShowMessage("No thrown light orbs nearby to recall");
    }

    public void AbsorbRecalledOrb(float refund)
    {
        lightAmount = Mathf.Min(maxLight, lightAmount + refund);
        GameHUD.RecordOrbRecalled();
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Shard"))
        {
            lightAmount = Mathf.Min(maxLight, lightAmount + 40f);
            GameHUD.RecordShardCollected();
            if (_obstacles != null) _obstacles.RegisterCollectedShard(other.gameObject, 6f);
            ShowMessage("+40 light");
        }
        else if (other.CompareTag("Checkpoint") && !IsActive(other.gameObject))
            ShowMessage($"Throw {checkpointCost - Charge(other.gameObject)} light orb(s) at the checkpoint to light it");
        else if (other.CompareTag("Finish") && !_won)
        {
            _won = true;
            if (_hud != null) _hud.RecordLevelComplete();
            _rb.linearVelocity = Vector2.zero;
            _cpPos = null;
        }
        else if (other.CompareTag("Enemy")) HandleEnemyContact(other);
    }

    void OnTriggerStay2D(Collider2D other)
    {
        if (!_won && other.CompareTag("Enemy")) HandleEnemyContact(other);
    }

    void HandleEnemyContact(Collider2D other)
    {
        var enemy = other.GetComponentInParent<Enemy>();
        bool fromAbove = transform.position.y > other.bounds.center.y + 0.2f && _rb.linearVelocity.y <= 0.5f;
        if (enemy != null && (_mass.IsSlamming || (IsHeavy && fromAbove)))
        {
            _mass.IsSlamming = false;
            enemy.Stun(3.5f);
            _rb.linearVelocity = new Vector2(_rb.linearVelocity.x, jumpVelocity * 0.78f);
            ShowMessage("Heavy Stomp!");
            return;
        }

        if (Time.time >= _invulnUntil) Respawn();
    }

    void OnCollisionEnter2D(Collision2D hit)
    {
        if (hit.gameObject.CompareTag("Barrier") && HasLight) ShowMessage("Shadow wall: break it with light, or slip through with no light");
    }

    void UpdateShadowWalls()
    {
        bool dark = !HasLight;
        if (dark == _phased) return;
        if (!dark && Physics2D.OverlapBox(_col.bounds.center, _col.bounds.size * 0.95f, 0f, shadowMask) != null) return;
        _phased = dark;
        foreach (var c in _shadowCols) if (c != null) Physics2D.IgnoreCollision(_col, c, dark);
        foreach (var s in _shadowSprites)
            if (s != null) { var col = s.color; col.a = dark ? 0.35f : 0.9f; s.color = col; }
        if (dark) ShowMessage("No light: you can slip through shadow walls");
    }

    bool IsActive(GameObject cp) => _cpPos.HasValue && Vector2.Distance(_cpPos.Value, cp.transform.position) < 0.01f;
    int Charge(GameObject cp) => _charge.TryGetValue(cp, out int n) ? n : 0;

    public bool FeedCheckpoint(GameObject cp)
    {
        if (IsActive(cp)) return false;
        int n = Charge(cp) + 1;
        _charge[cp] = n;
        if (n < checkpointCost)
        {
            LightUp(cp, (float)n / checkpointCost);
            ShowMessage($"Checkpoint {n}/{checkpointCost}");
            return true;
        }
        ActivateCheckpoint(cp);
        return true;
    }

    void ActivateCheckpoint(GameObject cp)
    {
        foreach (var other in GameObject.FindGameObjectsWithTag("Checkpoint"))
            if (other != cp && IsActive(other)) { LightUp(other, 0f); _charge.Remove(other); }
        lightAmount = Mathf.Max(lightAmount, checkpointMinLight);
        _cpScene = SceneManager.GetActiveScene().name;
        _cpPos = cp.transform.position;
        _cpLight = lightAmount;
        _charge.Remove(cp);
        LightUp(cp, 1f);
        if (_hud != null) _hud.TriggerCheckpointFlash();
        ShowMessage("Checkpoint lit");
    }

    static void LightUp(GameObject cp, float t)
    {
        bool on = t >= 1f;
        var l = cp.GetComponentInChildren<Light2D>();
        if (l != null)
        {
            l.pointLightOuterRadius = on ? 2.6f : 1.2f + 0.6f * t;
            l.pointLightInnerRadius = l.pointLightOuterRadius * GlowInner;
            l.intensity = on ? 1f : 0.5f + 0.3f * t;
            l.color = on ? new Color(0.6f, 1f, 0.75f) : new Color(1f, 0.8f, 0.5f);
        }
        var flame = cp.transform.Find("Flame");
        if (flame != null)
            flame.GetComponent<SpriteRenderer>().color = on ? new Color(0.5f, 1f, 0.7f) : Color.Lerp(new Color(1f, 0.6f, 0.25f), new Color(1f, 0.9f, 0.6f), t);
    }

    void Respawn()
    {
        if (!_won) GameHUD.RecordDeath();
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void RestartFromBeginning()
    {
        _cpPos = null;
        GameHUD.ResetRunStats();
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void ShowMessage(string msg, float seconds = 2f)
    {
        if (_hud != null) _hud.Show(msg, seconds);
    }

    void OnGUI()
    {
        if (_hud != null) _hud.DrawHUD(this);
    }
}
