using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

public class PlayerMovement : MonoBehaviour
{
    //values taken from tf2:
    //tickrate: 66.6...; a tick is 0.015 seconds
    private const float surfaceExtention = 0.03125f;
    private const float groundingHeight = 2f;
    private const float maxVelocity = 3500f;
    private const float leaveVelocity = 250f;
    private const float cosMaxWalkableAngle = 0.7f; //cos(45.58)
    private const float maxStepHeight = 18f;
    private const float minStepWidth = 0.04f;
    public float gravity = 800f;
    private const float jumpVelocity = 289f;
    public float bhopCap = 1.2f; //tf2 reduces your velocity to your walkspeed * 1.2 after a jump to nerf bhopping
    private const float walkSpeed = 300f;
    private const float airSpeed = 30f;
    public float classSpeedMod = 0.8f; //soldier
    private const float friction = 4f; //friction for regular ground, idk what ice uses, or if there even are ice physics in the engine
    private const float stopSpeed = 100f; //the speed at which friction will stop you, if your velocity is less than this, it will be set to 0 instead of being reduced by friction
    private const float duckSpeedMod = 1f / 3f; //tf2 reduces your max walking speed to 1/3 of your normal speed when ducking
    private const float backSpeedMod = 0.9f; //tf2 reduces your max walking speed to 90% when walking backwards
    private const float swimSpeedMod = 0.8f;
    private const float boundingBoxWidth = 48f; // or 49
    private const float standingBoundingBoxHeight = 82f; // or 83
    private const float duckingBoundingBoxHeight = 62f; // or 63
    private const float oldDuckingBoundingBoxHeight = 55f;
    private const float standingViewHeight = 68f; // 65 for scout, 75 for heavy and support classes
    private const float duckingViewHeight = 45f;
    //idk, dude, this game is weird: https://www.youtube.com/watch?v=AUPBC5W1KHo
    private const float minAimAssistDist = 200f;
    private const float maxAimAssistDist = 2000f;
    //projectile spawning location offsets from camera when firing
    private const float forwardProjectileOffset = 23.5f;
    private const float standingUpwardProjectileOffset = -3f;
    private const float duckingUpwardProjectileOffset = 8f;
    private const float stockRightwardProjectileOffset = 15f;
    private const float cowManglerRightwardProjectileOffset = 8f; 
    private const float originalRightwardProjectileOffset = 0f;

    private Vector3 velocity = new Vector3(0f, 0f, 0f);
    private GameObject ground = null;
    private Vector3 groundNormal = new Vector3(0f, 1f, 0f);
    private bool isCrouching = false;
    private bool jumping = false;
    public bool hardSpeedCap = true;
    private Transform cameraTf;
    public float groundAccel = 10f; //reach max speed in 0.1s
    public float airAccel = 10f;
    private BoxCollider boundingBox;
    public LayerMask colliderMask;

    private SweptTraces tracer;

    List<Vector3> poss = new List<Vector3>();
    List<Vector3> dirst = new List<Vector3>();

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        cameraTf = GetComponentInChildren<Camera>().GetComponent<Transform>();
        boundingBox = GetComponent<BoxCollider>();
        tracer = GetComponent<SweptTraces>();
        //Physics.defaultContactOffset = 0;
        // GroundCheck(this.gameObject.transform.position);
    }

    // FixedUpdate is called once per tick
    void FixedUpdate()
    {
        Vector3 newPos = this.gameObject.transform.position;

        //  1. If stuck, attempt to find free space and skip to step 14,
        if (UnsweptTrace(newPos))
        {
            //TODO: attempt to find free space
        }
        else //if stuck
        {
            //  2. If going up too fast, become airborne,
            if (velocity.y > leaveVelocity) //TODO: rising platforms influence this
            {
                ground = null;
            }

            //  3. Handle ducking,

            //  4. Apply half of gravity,
            AddHalfGravity();

            //  5. Handle jumping,
            if (ground != null && jumping && !isCrouching)
            {
                Jump();
            }

            //  6. Cap velocity,
            CapSpeed(maxVelocity);

            //  7. If on ground, zero out vertical velocity and apply friction,
            if (ground != null)
            {
                velocity = velocity - Vector3.Dot(velocity, groundNormal) * groundNormal; //TODO: this works on horizontal planes, but will need to be changed when we add slopes, walking down on them would be jittery
                Friction();
            }

            //  8. Accelerate,
            Accelerate();

            //  9. Movement and collisions,
            //TODO
            newPos = Move(4, Time.fixedDeltaTime, newPos);
            //handle step up on collision, step down at the end of frame

            // 10. Check for ground to stand on,
            newPos = GroundCheck(newPos);

            // 11. Apply other half of gravity,
            AddHalfGravity();

            // 12. If on ground, zero out vertical velocity,
            if (ground != null)
            {
                velocity = velocity - Vector3.Dot(velocity, groundNormal) * groundNormal; //TODO: this works on horizontal planes, but will need to be changed when we add slopes, walking down on them would be jittery
            }

            // 13. Cap velocity,
            CapSpeed(maxVelocity);

        } //if stuck in step 1., skip to step 14.

        // 14. Check for triggers to activate,
        // CheckTriggers(newPos);

        // 15. Update bounding box,
        transform.position = newPos;
        //transform.localScale = new Vector3(boundingBoxWidth, isCrouching ? duckingBoundingBoxHeight : standingBoundingBoxHeight, boundingBoxWidth);

        // 16. Shoot / detonate projectiles.



        jumping = false; //reset jump input, will be set to true again if the player presses the jump button before the next FixedUpdate
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetButtonDown("Jump") || Input.GetButton("Jump")) // || autoBHop && Input.GetButton("Jump")
        {
            jumping = true;
        }
    }

    // based on CGameMovement::Friction(...)
    void Friction()
    {
        Vector3 groundVel = Vector3.zero;

        if (ground != null)
        {
            MovingPlatform platform = ground.GetComponent<MovingPlatform>();
            if (platform != null)
            {
                groundVel = platform.GetCurrentVelocity();
            }
        }

        Vector3 relVelocity = velocity - groundVel;

        //Vector3 normal = groundNormal.sqrMagnitude > 0.001f ? groundNormal : Vector3.up;
        
        Vector3 lateralRelVelocity = relVelocity - Vector3.Dot(relVelocity, groundNormal) * groundNormal;
        float speed = lateralRelVelocity.magnitude;
        if (speed < 0.01f)
            return;

        float control = Mathf.Max(speed, stopSpeed);
        float drop = control * friction * Time.fixedDeltaTime;

        float newSpeed = Mathf.Max(speed - drop, 0f);
        if (newSpeed != speed)
        {
            lateralRelVelocity *= newSpeed / speed;
        }

        Vector3 normalComponent = Vector3.Dot(relVelocity, groundNormal) * groundNormal;

        velocity = lateralRelVelocity + normalComponent + groundVel;
    }

    void Jump()
    {
        //  1. Cap speed,
        CapSpeed(walkSpeed * classSpeedMod * bhopCap);

        //  2. Unground the player,
        ground = null;

        //  3. Apply jump velocity,
        velocity = new Vector3(velocity.x, velocity.y + jumpVelocity, velocity.z);
        //crouchjump version:
        //velocity = new Vector3(velocity.x, jumpVelocity, velocity.z);

        //  4. Apply half of gravity
        AddHalfGravity();
    }

    void AddHalfGravity()
    {
        velocity = velocity - (gravity * 0.5f * Time.fixedDeltaTime) * groundNormal; //TODO: if grounded on slopes this is weird, might slide down
    }

    void CapSpeed(float cap)
    {
        if (velocity.magnitude > cap) velocity = velocity.normalized * cap;
    }

    void Accelerate() 
    {
        Vector3 forward = cameraTf.forward;
        Vector3 right = cameraTf.right;

        //don't influence add_speed by making the normalized look down and let that component get lost later
        forward.y = 0f;
        right.y = 0f;

        //normalize, we only need directions
        forward.Normalize();
        right.Normalize();

        // Combine movement input
        Vector3 wishdir = (Input.GetAxisRaw("Horizontal") * right + Input.GetAxisRaw("Vertical") * forward).normalized;

        if (ground != null)
        {
            GroundAccelerate(wishdir, forward);
        }
        else
        {
            AirAccelerate(wishdir);
        }
        //watermovement later
        //this is weird, maybe like (as this option makes looking up and going forward + pressing jump move even more steeply up): (Input.GetAxisRaw("Horizontal") * right + Input.GetAxisRaw("Vertical") * forward + BoolToFloat(Input.GetButton("Jump")) * Vector3.up).normalized;
        //Vector3 waterWishdir = new Vector3(Input.GetAxisRaw("Horizontal") * right.x + Input.GetAxisRaw("Vertical") * forward.x , Mathf.Clamp(Input.GetAxisRaw("Horizontal") * right.y + Input.GetAxisRaw("Vertical") * forward.y + BoolToFloat(Input.GetButton("Jump")), -1f, 1f), Input.GetAxisRaw("Horizontal") * right.z + Input.GetAxisRaw("Vertical") * forward.z).normalized;
    
        // WaterAccelerate(waterWishdir);
    }

    void GroundAccelerate(Vector3 wishdir, Vector3 forward) 
    {
        
        Vector3 groundVel = Vector3.zero; //moving platform handling

        if (ground != null)
        {
            MovingPlatform platform = ground.GetComponent<MovingPlatform>();
            if (platform != null)
            {
                groundVel = platform.GetCurrentVelocity();
            }
        }

        if (hardSpeedCap && (velocity - groundVel).magnitude > walkSpeed * classSpeedMod) velocity = (velocity - groundVel).normalized * walkSpeed * classSpeedMod + groundVel; //not capspeed because groundmovement isn't a flat increase

        //weird ass code in quake, makes zigzagging and wallstrafing (as well as airstrafing, but different) work
        float currentSpeed = Vector3.Dot(velocity - groundVel, Vector3.ProjectOnPlane(wishdir, groundNormal).normalized);
        bool movingBackward = Vector3.Dot(wishdir, forward) < -0.5f;
        float wishSpeed = walkSpeed * classSpeedMod * (isCrouching ? duckSpeedMod : 1) * (movingBackward && !isCrouching ? backSpeedMod : 1);

        //this does way too much, but this is what clamp is for (the crouching and backwards check was added after this comment was already written, but it didn't stop me)
        float addSpeed = Mathf.Clamp(wishSpeed - currentSpeed, 0, groundAccel * wishSpeed * Time.fixedDeltaTime);

        //return new velocity
        velocity = velocity + addSpeed * Vector3.ProjectOnPlane(wishdir, groundNormal).normalized;
    }

    void AirAccelerate(Vector3 wishdir) 
    {
        //weird ass code in quake, makes airstrafing work
        float currentSpeed = Vector3.Dot(velocity, wishdir);

        //this does way too much, but this is what clamp is for
        float addSpeed = Mathf.Clamp(airSpeed - currentSpeed, 0, airAccel * airSpeed * Time.fixedDeltaTime);

        //return new velocity
        velocity = velocity + addSpeed * wishdir;
        
    }

    void WaterAccelerate(Vector3 wishdir) 
    {
        
    }

    Vector3 GroundCheck(Vector3 pos)
    {
        Vector3 ret = pos;
        float groundYVel = 0f;
        if (ground != null) {
            MovingPlatform platform = ground.GetComponent<MovingPlatform>();
            if (platform != null)
            {
                groundYVel = Mathf.Max(0, platform.GetCurrentVelocity().y); //to jump off fast moving platforms...
            }
        }
        if (velocity.y > leaveVelocity + groundYVel)
        {
            ground = null;
            return ret;
        }

        groundNormal = Vector3.up;
        
        Vector3 origin = pos + Vector3.down * (isCrouching ? duckingBoundingBoxHeight : standingBoundingBoxHeight) / 2 +
        Vector3.up / 2; // for the unity docs thing, so we cast from far enough up to have no overlap
        //"For colliders that overlap the sphere at the start of the sweep,
        //RaycastHit.normal is set opposite to the direction of the sweep,
        //RaycastHit.distance is set to zero,
        //and the zero vector gets returned in RaycastHit.point.
        //You might want to check whether this is the case in your particular query and perform additional queries to refine the result."
        //- UnityDocs: Physics.SphereCastAll
        //WTF?
        
        RaycastHit[] hits = Physics.BoxCastAll(
            origin,
            new Vector3(boundingBoxWidth, 1f, boundingBoxWidth) * 0.5f,
            Vector3.down,
            Quaternion.identity,
            groundingHeight,
            colliderMask,
            QueryTriggerInteraction.Ignore
        );

        GameObject found = null;
        float distance = 0f;
        float minDistance = 2f;

        foreach (RaycastHit hit in hits) {
            float dot = Vector3.Dot(hit.normal, Vector3.up);
            if (dot >= cosMaxWalkableAngle && hit.distance <= minDistance) {
                //bestDot = dot; //if looking for flattest instead of highest
                groundNormal = hit.normal;
                found = hit.collider.gameObject;
                distance = hit.distance;
                minDistance = hit.distance;
            }
        }
        
        if (ground != null && found != null)
        {
            ret = pos - Vector3.up * distance + Vector3.up * surfaceExtention;
        }

        ground = found;
        return ret;
    }

    //collision is weird in source games, code to mimic traces as described in the collision section of "Review.pdf"
    //these are weird, so wallbugs (along other things) behave similiarly to the source/quake engine's bsp collision (TODO: actually copy it in more detail)
    bool UnsweptTrace(Vector3 pos)
    {
        Collider[] colliders = Physics.OverlapBox(
            pos,
            new Vector3(boundingBoxWidth, standingBoundingBoxHeight, boundingBoxWidth) * 0.5f, //surfaceextention?, crouch?
            Quaternion.identity,
            colliderMask
        );
        for (int i = 0; i < colliders.Length; ++i) //check for intersects in GJK algorithm
        {
            if (tracer.GJKPointInside(colliders[i].gameObject.GetComponent<MeshCollider>(), new Vector3(boundingBoxWidth, standingBoundingBoxHeight, boundingBoxWidth) * 0.5f, pos)) //crouch; getcomponent, all colliders are meshcolliders, this should be faster
            {
                return true;
            }
        }
        return false;
    }

    //this is weird, has 2 version for leaves and brushes respectively: a <= b and a - b <= 0, so there are some rounding disagreements, causing bugs, which, for now, I will not replicate
    float SweptTrace(Vector3 startPos, Vector3 wishPos, out RaycastHit hit)
    {
        Vector3 wishmove = wishPos - startPos;
        float wishdist = wishmove.magnitude;
        Vector3 wishdir = new Vector3(wishmove.x, wishmove.y, wishmove.z).normalized;
        hit = new RaycastHit();

        //find all possible collisions along path (inaccurate, has to be corrected, checks larger area than should, gets false positives and 0-distance hits with bat values)
        RaycastHit[] hits = Physics.BoxCastAll(
            startPos,
            new Vector3(boundingBoxWidth, standingBoundingBoxHeight, boundingBoxWidth) * 0.5f, //TODO: handle crouch; height multiplyer is weird, unity thing, temporary
            wishdir,
            Quaternion.identity,
            wishdist,
            colliderMask,
            QueryTriggerInteraction.Ignore
        );

        //sort hits by distance, with bad slopes and directions 0-distance hits are actually either wrong or at a different distance, so we cannot just keep the first one
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        for (int i = 0; i < hits.Length; ++i)
        {
            if (tracer.GJKSweptIntersects(hits[i].collider.gameObject.GetComponent<MeshCollider>(), new Vector3(boundingBoxWidth, standingBoundingBoxHeight, boundingBoxWidth) * 0.5f, startPos, wishmove)) //crouch; getcomponent, all colliders are meshcolliders, this should be faster
            {
                hit = hits[0];
                return hits[0].distance / wishdist; //if 0 distance hit and have velocity towards the object, the hit is still counted...
            }
        }
        return 1f;
    }

    Vector3 Move(int depth, float timeLeft, Vector3 startPos)
    {
        Vector3 ret = startPos;
        if (depth < 0)
        {
            return ret;
        }
        Vector3 wishpos = startPos + velocity * timeLeft;
        RaycastHit hit;
        float completed = SweptTrace(startPos, wishpos, out hit);
        if (completed >= 1.0f)
        {
            return wishpos;
        }
        if (hit.distance == 0f) //overlap at start
        {
            //stuck in something, if by going towards endPos, we leave it, move to where it the box would fully leave the collider, that is the distance traveled
            //if cannot escape, just end with startPos
            //for now...
            //return Move(depth - 1, timeLeft - completed * timeLeft, startPos + (wishpos - startPos) * completed + surfaceExtention * hit.normal);
        }
        else
        {
            velocity = velocity - Vector3.Dot(velocity, hit.normal) * hit.normal;
        }

        return Move(depth - 1, timeLeft - completed * timeLeft, startPos + Max(new Vector3(0f, 0f, 0f), (wishpos - startPos) * completed + surfaceExtention * hit.normal, (wishpos - startPos).normalized));
    }
    // Notes: For colliders that overlap the box at the start of the sweep,
    // RaycastHit.normal is set opposite to the direction of the sweep,
    // RaycastHit.distance is set to zero,
    // and the zero vector gets returned in RaycastHit.point.
    // You might want to check whether this is the case in your particular query and perform additional queries to refine the result.
    // this can be useful for starting in a wall and leaving it and being fully stuck without being able to leave so we just return start
    
    float BoolToFloat(bool b)
    {
        return b ? 1f : 0f;
    }

    Vector3 Max(Vector3 a, Vector3 b, Vector3 along)
    {
        //problem: this is the opposite direction when too close, because boxcastall sucks
        return Vector3.Dot(along, a) > Vector3.Dot(along, b) ? a : b;
    }
}
