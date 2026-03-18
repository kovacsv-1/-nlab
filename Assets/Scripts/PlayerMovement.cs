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
    private GameObject dummyObject = null; //so SweptTrace doesn't need an insane amount of objects to be called 
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
    private GJKClosest gjkClosest;

    List<Vector3> poss = new List<Vector3>();
    List<Vector3> dirst = new List<Vector3>();

    class ColliderResolver  //to not call a lot of getcomponents
    {
        Dictionary<Collider, MeshCollider> map = new Dictionary<Collider, MeshCollider>();

        public MeshCollider ResolveCollider(Collider collider)
        {
            if (!map.ContainsKey(collider))
            {
                map.Add(collider, collider.gameObject.GetComponent<MeshCollider>());
            }
            return map[collider];
        }
    }

    ColliderResolver cr = new ColliderResolver();

    struct ColDist
    {
        public MeshCollider collider;
        public float dist;
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        cameraTf = GetComponentInChildren<Camera>().GetComponent<Transform>();
        boundingBox = GetComponent<BoxCollider>();
        tracer = GetComponent<SweptTraces>();
        gjkClosest = GetComponent<GJKClosest>();
        Application.targetFrameRate = 132;
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
            newPos = GetUnstuck(newPos);
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
            newPos = Move(4, Time.fixedDeltaTime, newPos);
            if (ground != null)
            {
                newPos = GroundCheck(newPos, maxStepHeight + surfaceExtention * 1.1f); // * 1.079925477505f); //step down (yes, this is stupid, but I already have it implemented, so it should be fine)
            }
            //handle step up on collision, step down at the end of frame

            // 10. Check for ground to stand on,
            newPos = GroundCheck(newPos, groundingHeight);

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

        if (Vector3.Dot(groundNormal, Vector3.up) < 1f)
        {
            velocity = velocity + (gravity * 0.5f * Time.fixedDeltaTime) * groundNormal;
            velocity = velocity - (gravity * 0.5f * Time.fixedDeltaTime) * Vector3.up; //so we don't jump towards the slope every time we jump
        }

        //  2. Unground the player,
        ground = null;
        groundNormal = Vector3.up;

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

    Vector3 GroundCheck(Vector3 pos, float checkDist)
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
        
        /*Vector3 origin = pos + Vector3.down * (isCrouching ? duckingBoundingBoxHeight : standingBoundingBoxHeight) / 2 +
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
            checkDist,
            colliderMask,
            QueryTriggerInteraction.Ignore
        );*/

        GameObject found = null;
        //float distance = 0f;
        //float minDistance = checkDist;
        RaycastHit hit = new RaycastHit();
        float completed = SweptTrace(pos, pos - Vector3.up * checkDist, out hit, out found);

        if (completed >= 1f)
        {
            ground = null;
            groundNormal = Vector3.up;
            return ret;
        }
        //Debug.Log(completed); //ALWAYS 0, HELP, IDK WHY, IT WORKS IN MOVE(...)
        //Debug.Log(found);


        /*foreach (RaycastHit hit in hits)
        {
            float dot = Vector3.Dot(hit.normal, Vector3.up);
            MeshCollider other = cr.ResolveCollider(hit.collider);
            if (hit.distance <= minDistance && tracer.GJKSweptIntersects(other, new Vector3(boundingBoxWidth, standingBoundingBoxHeight, boundingBoxWidth) * 0.5f, pos, Vector3.down * checkDist)
                /*&& !tracer.GJKSweptIntersects(other, new Vector3(boundingBoxWidth, standingBoundingBoxHeight, boundingBoxWidth) * 0.5f, pos, Vector3.down * 0f)/) //crouch; getcomponent, all colliders are meshcolliders, this should be faster
            {   //should really gjkshapecast for exact results, just the bpxcast often allows you to jump on walls, as it has an extention, and when close you get a 0 distance upwards facing hit
                groundNormal = Vector3.up;
                found = null;
                //bestDot = dot; //if looking for flattest instead of highest
                distance = hit.distance;
                minDistance = hit.distance;
                if (dot >= cosMaxWalkableAngle)
                {
                    groundNormal = hit.normal;
                    found = hit.collider.gameObject;
                } 
            }
        }*/
        
        if (Vector3.Dot(hit.normal, Vector3.up) >= cosMaxWalkableAngle)
        {
            groundNormal = hit.normal;
        }
        else
        {
            found = null;
        }

        if (ground != null && found != null)
        {
            ret = pos - Vector3.up * hit.distance + Vector3.up * surfaceExtention;
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
            if (tracer.GJKPointInside(cr.ResolveCollider(colliders[i]), new Vector3(boundingBoxWidth, standingBoundingBoxHeight, boundingBoxWidth) * 0.5f, pos)) //crouch; getcomponent, all colliders are meshcolliders, this should be faster
            {
                return true;
            }
        }
        return false;
    }

    Vector3 GetUnstuck(Vector3 pos)
    {
        for (int i = -1; i < 2; ++i)
        {
            for (int e = -1; e < 2; ++e)
            {
                for (int f = -1; f < 2; ++f)
                {
                    Vector3 testPos = new Vector3(pos.x + e, pos.y - i, pos.z + f); //check straight up first
                    if (!UnsweptTrace(testPos))
                    {
                        return testPos;
                    }
                }
            }
        }
        return pos;
    }

    //this is weird, has 2 version for leaves and brushes respectively: a <= b and a - b <= 0, so there are some rounding disagreements, causing bugs, which, for now, I will not replicate
    float SweptTrace(Vector3 startPos, Vector3 wishPos, out RaycastHit hit, out GameObject collidedObject)
    {
        Vector3 wishmove = wishPos - startPos;
        float wishdist = wishmove.magnitude;
        Vector3 wishdir = wishmove.normalized;
        hit = new RaycastHit();
        collidedObject = null;

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

        if (hits.Length == 0)
        {
            hit.normal = Vector3.zero;
            return 1f;
        }

        //sort hits by distance, with bad slopes and directions 0-distance hits are actually either wrong or at a different distance, so we cannot just keep the first one
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        List<ColDist> collidedWith = new List<ColDist>();

        for (int i = 0; i < hits.Length; ++i)
        {
            MeshCollider other = cr.ResolveCollider(hits[i].collider);
            if (tracer.GJKSweptIntersects(other, new Vector3(boundingBoxWidth, standingBoundingBoxHeight, boundingBoxWidth) * 0.5f, startPos, wishmove.normalized * wishdist)) //crouch; getcomponent, all colliders are meshcolliders, this should be faster
            { //this should work, but just never would run with the check
                ColDist info = new ColDist();
                info.collider = other;
                info.dist = hits[i].distance;
                collidedWith.Add(info);
            }
        }

        if (collidedWith.Count == 0)
        {
            hit.normal = Vector3.zero;
            return 1f;
        }

        //List.Sort(collidedWith, (a, b) => a.dist.CompareTo(b.dist)); //only checking true hits, shouldn't need to sort as that is the order added
        
        //interate over all collidedwith and gjkclosest + step in the wishdir to distance
        //until min found hit distance < next estimated hit distance + 1f (or another large margin)
        //and return the min distance hit's values with
        float bestDistance = float.PositiveInfinity;
        GJKHit bestHit = new GJKHit();
        GameObject bestObject = null;
        foreach (var col in collidedWith)
        {
            GJKHit gjk = gjkClosest.GJKShapeCast(
                col.collider,
                new Vector3(boundingBoxWidth, standingBoundingBoxHeight, boundingBoxWidth) * 0.5f,
                startPos,
                wishmove
            );
            // If this collider produces a hit and it's closer than the current best, update
            if (gjk.hit && gjk.distance < bestDistance)
            {
                bestDistance = gjk.distance;
                bestHit = gjk;
                bestObject = col.collider.gameObject;
            }
        }
        if (bestHit.hit)
        {
            hit.distance = bestHit.distance;
            hit.point = bestHit.point;
            hit.normal = -bestHit.normal;
            collidedObject = bestObject;
            return bestDistance / wishdist;
        }
        else
        {
            hit.normal = Vector3.zero;
            return 1f;
        }

        //return hits[0].distance / wishdist; //if 0 distance hit and have velocity towards the object, the hit is still counted...
        //return hit.distance / wishdist;
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
        float completed = SweptTrace(startPos, wishpos, out hit, out dummyObject);
        if (completed >= 1.0f)
        {
            return startPos + Max(new Vector3(0f, 0f, 0f), (wishpos - startPos) + surfaceExtention * hit.normal, (wishpos - startPos).normalized);
        }
        Vector3 remove = Vector3.Project(velocity, hit.normal);
        if (Vector3.Dot(remove, hit.normal) < 0f)
        {
            remove *= -1f;
        }
        if (hit.distance == 0f) //overlap at start
        {
            //stuck in something, if by going towards endPos, we leave it, move to where it the box would fully leave the collider, that is the distance traveled
            //if cannot escape, just end with startPos
            //for now...
            //return Move(depth - 1, timeLeft - completed * timeLeft, startPos + (wishpos - startPos) * completed + surfaceExtention * hit.normal);
            //velocity = velocity + Math.Abs(Vector3.Dot(velocity, hit.normal)) * hit.normal;
            velocity = velocity + remove;
        }
        else
        {
            //velocity = velocity + Math.Abs(Vector3.Dot(velocity, hit.normal)) * hit.normal;
            velocity = velocity + remove;
        }
        //if collided wall, move playerup 18 units, forwards 0.04 units and stuckcheck+groundcheck to handle stairs
        //if it was a stairsetp, recurse without decreasing depth, so we don't just stop the movement after 4 stps when moving fast

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
        //return b;
    }
}
