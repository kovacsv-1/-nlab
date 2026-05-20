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
    public float explosionRadius = 121f;
    public float baseDamage = 90f;
    public float falloff = 0.5f;
    public GameObject owner;
    public Vector3 startOffset = new Vector3(23.5f, 0f, -3f); //(forward, right, up) for the original, for others see https://www.youtube.com/watch?v=UFtZMIWt0WI
    public float crouchingUpOffset = 8f;
    
    public PlayerMovement playerMovement;

    private Vector3 collisionPoint;
    private Vector3 collisionNormal;
    private bool hasCollided = false;

    public void setDir(Vector3 facing) {
        dir = facing; //cam.forward in movement
        //rb.linearVelocity = dir * rocketSpeed;
        selfTransform.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(90f, 0f, 0f);
    }

    public void applyOffset(Transform cam) { //call when spawning
        selfTransform.position += startOffset.x * cam.forward; //I have to do this here because Start() runs AFTER this gets called for some reason
        selfTransform.position += startOffset.y * cam.right;
        float upOffset = owner.GetComponent<PlayerMovement>().GetCrouching() ? crouchingUpOffset : startOffset.z;
        selfTransform.position += upOffset * cam.up;
    }

    public void Step()
    {
        //raycast and use gjk stuff where possible
    }

    void FixedUpdate()
    {
        selfTransform.position += dir * rocketSpeed * Time.fixedDeltaTime;
        selfTransform.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(90f, 0f, 0f);
    }

    /* rigidbody solution, absolutely wrong, probably won't use particles
    void OnTriggerEnter(Collider other) {
        if ((passThrough.value & (1 << other.gameObject.layer)) > 0) //bitwise and for each bit of the mask and the layer's number (this is how masks work) to check for if layer is masked
            return;
        if (!((noNade.value & (1 << other.gameObject.layer)) > 0)) {
            Explode();
        }
        Destroy(this.gameObject);
    }

    void OnCollisionEnter(Collision other)
    {
        if (hasCollided) return;

        if ((passThrough.value & (1 << other.gameObject.layer)) > 0) //bitwise and for each bit of the mask and the layer's number (this is how masks work) to check for if layer is masked
            return;
        
        if (!((noNade.value & (1 << other.gameObject.layer)) > 0)) {
            // Get collision data
            ContactPoint contact = other.contacts[0];
            collisionPoint = contact.point;
            collisionNormal = contact.normal;
            hasCollided = true;
            
            // Move rocket 1 HU away from surface
            float moveDistance = 1f * owner.GetComponent<SimpleMovement>().HUToMeters;
            transform.position = collisionPoint + collisionNormal * moveDistance;
            
            // Stop movement and explode
            rb.linearVelocity = Vector3.zero;
            Explode();
        }

        Destroy(this.gameObject);
    }

    public void setDir(Vector3 facing) {
        dir = facing; //cam.forward in simplemovement
        rb.linearVelocity = dir * rocketSpeed;
    }

    public void applyOffset(Transform cam) { //call when spawning
        selfTransform.position += startOffset.x * cam.forward * HUToMeters; //I have to do this here because Start() runs AFTER this gets called for some reason
        selfTransform.position += startOffset.y * cam.right * HUToMeters;
        float upOffset = owner.GetComponent<SimpleMovement>().crouching ? crouchingUpOffset : startOffset.z;
        selfTransform.position += upOffset * cam.up * HUToMeters;
    }

    void Explode()
    {
        // Adjust explosion position to be 1 HU away from wall
        Vector3 explosionPosition = transform.position;
        
        // Create explosion effect at corrected position
        Instantiate(explosionEffect, explosionPosition, Quaternion.identity);
        
        // Get player reference
        GameObject player = GameObject.FindGameObjectWithTag("Player"); //replace this with a sphere collider at some point
        if (!player) return;
        
        // Calculate both possible damage points
        BoxCollider playerCollider = player.GetComponent<BoxCollider>();
        Vector3 playerCenter = player.transform.position + playerCollider.center;
        Vector3 playerBottom = playerCenter  - new Vector3(0, playerCollider.size.y/2, 0);
        
        // Get closest point to explosion
        float distanceToCenter = Vector3.Distance(explosionPosition, playerCenter);
        float distanceToBottom = Vector3.Distance(explosionPosition, playerBottom);
        Vector3 playerPos = distanceToBottom < distanceToCenter ? playerBottom : playerCenter;
        
        // Line of sight check from corrected position
        if (!HasLineOfSight(explosionPosition, playerPos))
            return;
        
        // Only affect owner
        if (player == owner)
        {
            CalculateSelfDamage(player, Vector3.Distance(explosionPosition, playerPos)); //distanceToBottom //this may or may not be calculated to middle bottom of bounding box (I don't think it is, but it is weird (it likely is))
        }
    }

    void CalculateSelfDamage(GameObject player, float distance)
    {
        playerMovement = player.GetComponent<SimpleMovement>();
        if (!playerMovement) return;

        float damage = CalculateDamage(distance);
        
        BoxCollider playerCollider = player.GetComponent<BoxCollider>();
        Vector3 playerPos = player.transform.position + playerCollider.center; // - new Vector3(0, playerCollider.size.y/2, 0);

        Vector3 knockback = CalculateKnockback(playerPos, damage); //this is calculated to feet position
        
        playerMovement.AddVelocity(knockback);
        // Apply damage to player health here
    }

    float CalculateDamage(float distance)
    {
        if (distance > explosionRadius * HUToMeters) return 0; //to be safe
        // TF2's damage falloff formula
        float distanceFactor = Mathf.Clamp01(distance / (explosionRadius * HUToMeters)); //* HUToMeters as shooting a wall too close would make this run before Start()...
        float damage = baseDamage * (1 - (1 - falloff) * distanceFactor);
        
        // Apply air resistance (tf_damagescale_self_soldier)
        if (!playerMovement.isGrounded && playerMovement.waterLevel == 0)
        {
            damage *= 0.6f;
        }
        
        return damage;
    }

    Vector3 CalculateKnockback(Vector3 playerPosition, float damage)
    {
        // Volume multiplier (TF2 crouching multiplier)
        float volumeMultiplier = playerMovement.crouching ? 1.4909f : 1f;
        
        // Scale factor (ground vs air)
        float scale = playerMovement.isGrounded ? 5f : 10f;
        
        // Force magnitude calculation
        float forceMagnitude = Mathf.Min(1000f, damage * volumeMultiplier * scale);
        
        // Direction calculation (from 10 HU below rocket)
        Vector3 forceOrigin = transform.position - Vector3.up * (10f * HUToMeters);
        Vector3 forceDir = (playerPosition - forceOrigin).normalized;

        return forceDir * forceMagnitude * HUToMeters;
    }

    bool HasLineOfSight(Vector3 explosionPoint, Vector3 playerPos)
    {
        if (owner.GetComponent<BoxCollider>().bounds.Contains(explosionPoint)) return true;
        // Check both possible damage points
        RaycastHit hit;
        Vector3 point = owner.GetComponent<BoxCollider>().ClosestPoint(explosionPoint);
        float distance = Vector3.Distance(explosionPoint, point);
        
        // Cast ray to center point
        return (!Physics.Raycast(explosionPoint, (point - explosionPoint).normalized, out hit, distance, blockSplash));
    }
    */
}
