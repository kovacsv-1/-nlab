using UnityEngine;

public class RocketScript : MonoBehaviour
{

    public Vector3 dir;

    public float rocketSpeed = 1100f;

    public LayerMask passThrough;
    public LayerMask noNade;
    public LayerMask blockSplash;
    public Transform selfTransform;

    public GameObject explosionEffect;
    public GameObject trailEffect;
    public float explosionRadius = 121f; //grenades 146, stickies 146, charged cow mangler 160.93 (1.33 * normal), but it slowes you down to 80 units/s when charging
    public float baseDamage = 90f; //100 for grenades, 120 for stickies
    public float falloff = 0.5f;
    public GameObject owner;
    public Vector3 startOffset = new Vector3(23.5f, 0f, -3f); //(forward, right, up) for the original, for others see https://www.youtube.com/watch?v=UFtZMIWt0WI
    public float crouchingUpOffset = 8f;
    
    public PlayerMovement playerMovement;

    private Vector3 collisionPoint;
    private Vector3 collisionNormal;
    private bool hasCollided = false;

    public void setDir(Vector3 facing) {
        dir = facing;
        selfTransform.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(90f, 0f, 0f);
    }

    public void applyOffset(Transform cam) { //call when spawning
        selfTransform.position += startOffset.x * cam.forward; //I have to do this here because Start() runs AFTER this gets called for some reason
        selfTransform.position += startOffset.y * cam.right;
        float upOffset = owner.GetComponent<PlayerMovement>().GetCrouching() ? crouchingUpOffset : startOffset.z;
        selfTransform.position += upOffset * cam.up;
    }

    public void Step(Vector3 playerPos)
    {
        if (Physics.Raycast(
            selfTransform.position, 
            dir, 
            out RaycastHit hit, 
            rocketSpeed * Time.fixedDeltaTime, 
            ~passThrough
        ))
        {
            float traversed = hit.distance / (rocketSpeed * Time.fixedDeltaTime);
            for (int i = 0; i * 0.25f < traversed; ++i)
            {
                Instantiate(trailEffect, selfTransform.position + traversed * dir * rocketSpeed * Time.fixedDeltaTime * (float)i * 0.25f, Quaternion.identity);
            }
            selfTransform.position += dir * hit.distance;
            if (((1 << hit.collider.gameObject.layer) & noNade) == 0)
            {
                Explode(playerPos, hit.normal);
            }
            Destroy(this.gameObject);
            return;
        }
        for (int i = 0; i < 4; ++i)
        {
            Instantiate(trailEffect, selfTransform.position + dir * rocketSpeed * Time.fixedDeltaTime * (float)i * 0.25f, Quaternion.identity);
        }
        selfTransform.position += dir * rocketSpeed * Time.fixedDeltaTime;
        selfTransform.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(90f, 0f, 0f);
    }

    void Explode(Vector3 playerPos, Vector3 hitNormal) //only doing self damage for now, regular should have rampup and falloff based on distance to owner as well, and collect enemies with a spherecast which gets recalculated like the damage is calculated here
    {
        Vector3 explosionPosition = selfTransform.position - hitNormal;
        Instantiate(explosionEffect, explosionPosition, Quaternion.identity);

        Vector3 playerBottom = playerPos - playerMovement.playerBounds().y * Vector3.up / 2f;

        float distanceToCenter = Vector3.Distance(explosionPosition, playerPos);
        float distanceToBottom = Vector3.Distance(explosionPosition, playerBottom);
        Vector3 calcPos = distanceToBottom < distanceToCenter ? playerBottom : playerPos;

        if (HasLineOfSight(explosionPosition, playerPos))
        {
            float damage = CalculateDamage(Vector3.Distance(explosionPosition, playerPos));
            playerMovement.TakeDamage(damage, explosionPosition, owner, playerPos);
        }

        //gather all objects on layer of enemy players and do this calculation for all with their positions and bounds instead of a passed position value
    }

    bool HasLineOfSight(Vector3 explosionPoint, Vector3 playerPos)
    {
        if (PointInsidePlayer(explosionPoint, playerPos)) return true;
        // Check both possible damage points
        RaycastHit hit;
        Vector3 point = PointOnPlayer(explosionPoint, playerPos);
        float distance = Vector3.Distance(explosionPoint, point);

        // Cast ray to center point
        return !Physics.Raycast(explosionPoint, (point - explosionPoint).normalized, out hit, distance, blockSplash); //only geometry can block splash damage, not players, so don't need to check if the player would block his own self-damage
    }

    bool PointInsidePlayer(Vector3 point, Vector3 playerPos)
    {
        Vector3 playerBound = playerMovement.playerBounds();
        if (point.x < playerPos.x - playerBound.x / 2 || point.x > playerPos.x + playerBound.x / 2) return false;
        if (point.y < playerPos.y - playerBound.y / 2 || point.y > playerPos.y + playerBound.y / 2) return false;
        if (point.z < playerPos.z - playerBound.z / 2 || point.z > playerPos.z + playerBound.z / 2) return false;
        return true;
    }

    Vector3 PointOnPlayer(Vector3 point, Vector3 playerPos)
    {
        Vector3 playerBound = playerMovement.playerBounds();
        float x = Mathf.Clamp(point.x, playerPos.x - playerBound.x / 2, playerPos.x + playerBound.x / 2);
        float y = Mathf.Clamp(point.y, playerPos.y - playerBound.y / 2, playerPos.y + playerBound.y / 2);
        float z = Mathf.Clamp(point.z, playerPos.z - playerBound.z / 2, playerPos.z + playerBound.z / 2);
        return new Vector3(x, y, z);
    }

    float CalculateDamage(float distance)
    {
        if (distance > explosionRadius) return 0; //to be safe
        // TF2's damage falloff formula for explosions, there is a regular damage falloff with the explosion point and playerpos distance when calculating damage to others, and then this still applies
        float damage = baseDamage * (1f - (0.5f * Mathf.Min(distance / explosionRadius, 1f))); //baseDamage should be influenced by rampup and falloff (the other falloff)
        
        return damage;
    }
}
