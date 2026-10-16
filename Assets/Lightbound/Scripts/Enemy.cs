using UnityEngine;

/// <summary>
/// Patrols continuously from one end of its land to the farthest other end.
/// Crawler: walks at a steady constant speed all the way to each end of the platform, turning only at ledges or ground-level walls.
/// Flyer: glides smoothly in a continuous wave patrol.
/// The child "Hurtbox" trigger (tag Enemy) is what hurts the player; it's switched off while stunned.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class Enemy : MonoBehaviour
{
    public bool flyer;
    public float patrolSpeed = 1.5f;
    public float playerPullSpeed = 1.5f;
    public float lureSpeed = 2.2f;
    public float lureRange = 10f;
    public LayerMask groundMask;
    public Collider2D hurtbox;

    Rigidbody2D _rb;
    Collider2D _col;
    Vector2 _home;
    int _dir = 1;
    float _stunnedUntil;
    float _stuckTimer;
    float _lastX;
    float _lastTurnAt = -10f;

    void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _col = GetComponent<Collider2D>();
        _home = transform.position;
        _lastX = _home.x;

        // Ensure enemies never block each other on the same platform
        Physics2D.IgnoreLayerCollision(gameObject.layer, gameObject.layer, true);
    }

    public bool IsStunned => Time.time < _stunnedUntil;

    public void Stun(float seconds, bool squash = false)
    {
        _stunnedUntil = Mathf.Max(_stunnedUntil, Time.time + seconds);
    }

    void FixedUpdate()
    {
        bool stunned = Time.time < _stunnedUntil;
        if (hurtbox != null) hurtbox.enabled = !stunned;
        if (stunned)
        {
            _rb.linearVelocity = flyer ? Vector2.zero : new Vector2(0f, _rb.linearVelocity.y);
            return;
        }

        Vector2 pos = transform.position;
        if (flyer)
        {
            Vector2? target = null;
            float speed = patrolSpeed;
            float best = lureRange;
            foreach (var lure in LightOrb.Lures)
            {
                if (lure == null || !lure.IsLure) continue;
                float d = Vector2.Distance(pos, lure.transform.position);
                if (d < best) { best = d; target = lure.transform.position; speed = lureSpeed; }
            }
            Fly(pos, target, speed);
        }
        else
        {
            // Crawlers patrol at a steady constant speed from one farthest end of the land to the other
            Crawl(pos, patrolSpeed);
        }
    }

    void Crawl(Vector2 pos, float speed)
    {
        if (_dir == 0) _dir = 1;

        if (Time.time - _lastTurnAt > 0.2f && IsAtLandEndOrWall(pos, _dir))
        {
            _dir = -_dir;
            _lastTurnAt = Time.time;
            _stuckTimer = 0f;
        }

        // Failsafe if physically blocked by any ground-level obstacle
        if (Mathf.Abs(pos.x - _lastX) < 0.002f)
        {
            _stuckTimer += Time.fixedDeltaTime;
            if (_stuckTimer > 0.25f)
            {
                _dir = -_dir;
                _lastTurnAt = Time.time;
                _stuckTimer = 0f;
            }
        }
        else
        {
            _stuckTimer = 0f;
        }
        _lastX = pos.x;

        _rb.linearVelocity = new Vector2(_dir * speed, _rb.linearVelocity.y);
    }

    bool IsAtLandEndOrWall(Vector2 pos, int dir)
    {
        float halfW = _col != null ? _col.bounds.extents.x : 0.45f;
        float halfH = _col != null ? _col.bounds.extents.y : 0.35f;

        // 1. Check if we reached the farthest edge of the land (ledge ahead)
        Vector2 ledgeOrigin = new Vector2(pos.x + dir * (halfW + 0.1f), pos.y);
        bool groundAhead = Physics2D.Raycast(ledgeOrigin, Vector2.down, halfH + 0.65f, groundMask);
        if (!groundAhead) return true;

        // 2. Check for a ground-level wall (e.g. Barrier or Cracked Monolith), ignoring elevated overhangs
        Vector2 lowOrigin = new Vector2(pos.x, pos.y - halfH * 0.45f);
        RaycastHit2D wallHit = Physics2D.Raycast(lowOrigin, new Vector2(dir, 0f), halfW + 0.15f, groundMask);
        if (wallHit.collider != null && wallHit.collider.gameObject.name != "SqueezeGate_Overhang")
            return true;

        return false;
    }

    void Fly(Vector2 pos, Vector2? target, float speed)
    {
        Vector2 goal = target ?? _home + new Vector2(Mathf.Sin(Time.time * 0.5f) * 2.5f, 0f);
        _rb.MovePosition(Vector2.MoveTowards(pos, goal, speed * Time.fixedDeltaTime));
    }
}
