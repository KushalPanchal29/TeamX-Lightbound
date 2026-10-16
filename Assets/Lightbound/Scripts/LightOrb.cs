using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Thrown light. Destroys anything tagged "Barrier", stuns enemies it passes through, charges checkpoints,
/// and when it lands it becomes a lure that enemies walk/fly toward while its light shrinks away.
/// Can also be recalled back into the player's body (Right-Click / E) to restore light and size.
/// </summary>
public class LightOrb : MonoBehaviour
{
    public static readonly List<LightOrb> Lures = new List<LightOrb>();
    public static readonly List<LightOrb> ActiveOrbs = new List<LightOrb>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Lures.Clear();
        ActiveOrbs.Clear();
    }

    public Transform lightMask;          // child with a soft Point Light 2D
    public float lureTime = 9f;
    public float stunTime = 2f;
    public float maxFlightTime = 2.5f;   // hangs in the air as a lure if it never hits anything
    public float recallSpeed = 24f;

    public bool IsLure { get; private set; }
    public bool IsRecalling { get; private set; }
    float _landedAt, _thrownAt;
    float _recallRefund;
    Light2D _light;
    float _startRadius;

    public float RemainingFraction => IsLure ? Mathf.Clamp01(1f - (Time.time - _landedAt) / lureTime) : 1f;

    void Awake()
    {
        _thrownAt = Time.time;
        if (lightMask != null) _light = lightMask.GetComponent<Light2D>();
        if (_light != null) _startRadius = _light.pointLightOuterRadius;
    }

    void OnEnable()
    {
        _thrownAt = Time.time;
        if (!ActiveOrbs.Contains(this)) ActiveOrbs.Add(this);
    }

    void OnDisable()
    {
        Lures.Remove(this);
        ActiveOrbs.Remove(this);
    }

    /// <summary>Pulls this orb back to the player to restore their light and physical size.</summary>
    public void Recall(float refund)
    {
        if (IsRecalling) return;
        IsRecalling = true;
        IsLure = false;
        _recallRefund = refund;
        Lures.Remove(this);
        CancelInvoke();
        var col = GetComponent<Collider2D>();
        if (col != null) col.enabled = false;
        var rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.bodyType = RigidbodyType2D.Kinematic;
        }
    }

    void OnCollisionEnter2D(Collision2D hit)
    {
        if (IsLure || IsRecalling) return;
        if (hit.gameObject.CompareTag("Barrier"))
        {
            Destroy(hit.gameObject);
            Destroy(gameObject);
            return;
        }
        Land();
    }

    void Land()
    {
        if (IsRecalling) return;
        var rb = GetComponent<Rigidbody2D>();
        rb.linearVelocity = Vector2.zero;
        rb.bodyType = RigidbodyType2D.Kinematic;
        IsLure = true;
        _landedAt = Time.time;
        Lures.Add(this);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (IsRecalling) return;
        // a checkpoint absorbs the orb (it needs 2 to light up); an already-lit one lets it fly through
        if (!IsLure && other.CompareTag("Checkpoint") && Player.Instance != null && Player.Instance.FeedCheckpoint(other.gameObject))
        {
            Destroy(gameObject);
            return;
        }
        var enemy = other.GetComponentInParent<Enemy>();
        if (enemy != null) enemy.Stun(stunTime);
    }

    void Update()
    {
        if (IsRecalling)
        {
            if (Player.Instance == null) { Destroy(gameObject); return; }
            Vector3 target = Player.Instance.transform.position;
            transform.position = Vector3.MoveTowards(transform.position, target, recallSpeed * Time.deltaTime);
            if (_light != null)
            {
                _light.pointLightOuterRadius = Mathf.Lerp(_light.pointLightOuterRadius, _startRadius * 0.7f, Time.deltaTime * 10f);
                _light.pointLightInnerRadius = _light.pointLightOuterRadius * Player.GlowInner;
            }
            if (Vector2.Distance(transform.position, target) < 0.35f)
            {
                Player.Instance.AbsorbRecalledOrb(_recallRefund);
                Destroy(gameObject);
            }
            return;
        }

        if (!IsLure && Time.time - _thrownAt > maxFlightTime) Land();
        if (IsLure)
        {
            float elapsed = Time.time - _landedAt;
            if (elapsed >= lureTime)
            {
                Destroy(gameObject);
                return;
            }
            if (_light != null)
            {
                float r = _startRadius * Mathf.Max(0f, 1f - elapsed / lureTime);
                _light.pointLightOuterRadius = r;
                _light.pointLightInnerRadius = r * Player.GlowInner;
            }
        }
    }
}
