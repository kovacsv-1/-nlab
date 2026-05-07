using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    //values taken from tf2:
    //tickrate: 66.6...; a tick is 0.015 seconds
    private const float surfaceExtention = 0.03125f;
    private const float groundExtention = 0.705f;
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
    private const float waterFriction = 1f;
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
    public float waterAccel = 10f;
    private BoxCollider boundingBox;
    public LayerMask colliderMask;
    public LayerMask waterMask;
    public LayerMask triggerMask;

    private SweptTraces tracer;
    private GJKClosest gjkClosest;

    List<Vector3> poss = new List<Vector3>();
    List<Vector3> dirst = new List<Vector3>();

    public int crouchAnimLength = 6; // 6/66.6...s to crouch/uncrouch 
    private int currentCrouchFrame = 0; // 0 -> fully standing; crouchAnimLength -> fully crouched
    private int maxAirCrouches = 2;
    private int usedAirCrouches = 0;
    private int jumpLockOut = 3;

    
    public List<GameObject> waterBodies;
    private float camY = 0f;

    private int waterLevel = 0;

    public bool autoBHop = false;

    //TODO: get a mononbehaviour of ground speed, water current and trigger activation
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

    ColliderResolver colliderResolver = new ColliderResolver();
    ColliderResolver waterResolver = new ColliderResolver();
    ColliderResolver triggerResolver = new ColliderResolver();

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
    }

    // FixedUpdate is called once per tick
    void FixedUpdate()
    {
        if (jumpLockOut < 3)
        {
            jumpLockOut++;
        }
        Vector3 newPos = this.gameObject.transform.position;
        EvaluateWater();

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
            newPos = HandleCrouch(newPos);

            //  4. Apply half of gravity,
            AddHalfGravity();

            //  5. Handle jumping,
            if (ground != null && waterLevel < 2 && jumping && !isCrouching && jumpLockOut >= 3)
            {
                Jump();
            }

            //  6. Cap velocity,
            CapSpeed(maxVelocity);

            //  7. If on ground, zero out vertical velocity and apply friction,
            if (waterLevel > 1)
            {
                WaterFriction();
            } else if (ground != null)
            {
                velocity = velocity - Vector3.Dot(velocity, groundNormal) * groundNormal; //TODO: this works on horizontal planes, but will need to be changed when we add slopes, walking down on them would be jittery
                Friction();
            }

            //  8. Accelerate,
            Accelerate();

            //  9. Movement and collisions,
            newPos = Move(4, Time.fixedDeltaTime, newPos);
            if (ground != null && waterLevel < 2)
            {
                newPos = GroundCheck(newPos, maxStepHeight + groundExtention * 1.1f); // * 1.079925477505f); //step down (yes, this is stupid, but I already have it implemented, so it should be fine)
            }
            //handle step up on collision, step down at the end of frame

            // 10. Check for ground to stand on,
            newPos = GroundCheck(newPos, groundingHeight);

            // 11. Apply other half of gravity,
            AddHalfGravity();

            // 12. If on ground, zero out vertical velocity,
            if (ground != null && waterLevel < 2)
            {
                velocity = velocity - Vector3.Dot(velocity, groundNormal) * groundNormal; //TODO: this works on horizontal planes, but will need to be changed when we add slopes, walking down on them would be jittery
            }

            // 13. Cap velocity,
            CapSpeed(maxVelocity);

        } //if stuck in step 1., skip to step 14.

        // 14. Check for triggers to activate,
        newPos = HandleTriggers(newPos); //todo make triggers have different effects

        // 15. Update bounding box,
        transform.position = newPos;
        camY = newPos.y - playerBounds().y / 2f + (standingViewHeight - (standingViewHeight - duckingViewHeight) * currentCrouchFrame / crouchAnimLength);
        cameraTf.position = new Vector3(newPos.x, camY, newPos.z);
        //transform.localScale = new Vector3(boundingBoxWidth, isCrouching ? duckingBoundingBoxHeight : standingBoundingBoxHeight, boundingBoxWidth);

        // 16. Shoot / detonate projectiles.

        jumping = false; //reset jump input, will be set to true again if the player presses the jump button before the next FixedUpdate
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetButtonDown("Jump") || autoBHop && Input.GetButton("Jump"))
        {
            jumping = true;
        }
    }

    // based on CGameMovement::Friction(...)
    void Friction()
    {
        Vector3 groundVel = Vector3.zero;

        Vector3 relVelocity = velocity - groundVel;
        
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

    void WaterFriction()
    {
        float speed = velocity.magnitude;
        if (speed < 0.01f)
            return;

        float stopSpeed = 100f;

        float control = Mathf.Max(speed, stopSpeed);
        float drop = control * waterFriction * Time.fixedDeltaTime;

        float newSpeed = Mathf.Max(speed - drop, 0f);
        if (newSpeed != speed)
        {
            velocity *= newSpeed / speed;
        }
    }

    void Jump()
    {
        usedAirCrouches = 0;
        jumpLockOut = 0;
        //  1. Cap speed,
        CapSpeed(walkSpeed * classSpeedMod * bhopCap);

        if (Vector3.Dot(groundNormal, Vector3.up) < 1f)
        {   //so we don't jump towards the slope every time we jump
            velocity = velocity + (gravity * 0.5f * Time.fixedDeltaTime) * groundNormal;
            velocity = new Vector3(velocity.x, 0f, velocity.z);
            velocity = velocity - (gravity * 0.5f * Time.fixedDeltaTime) * Vector3.up;
        }

        //  2. Unground the player,
        ground = null;
        groundNormal = Vector3.up;

        //  3. Apply jump velocity,
        if (currentCrouchFrame > 0 && currentCrouchFrame < crouchAnimLength)
        {
            velocity = new Vector3(velocity.x, jumpVelocity, velocity.z);
        }
        else 
        {
            velocity = new Vector3(velocity.x, velocity.y + jumpVelocity, velocity.z);
        }

        //  4. Apply half of gravity
        AddHalfGravity();
    }

    void AddHalfGravity()
    {
        if (waterLevel < 2)
        {
            velocity = velocity - (gravity * 0.5f * Time.fixedDeltaTime) * groundNormal; //TODO: if grounded on slopes this is weird, might slide down
        }
    }

    void CapSpeed(float cap)
    {
        if (velocity.magnitude > cap) velocity = velocity.normalized * cap;
    }

    //TODO: get wishdir, waterwishdir, jumping and the checks for jumping in water from another script (like bots or network)
    void Accelerate()
    {
        Vector3 forward = cameraTf.forward;
        Vector3 right = cameraTf.right;

        //this is how tf2 works, it is kinda dumb
        Vector3 waterWishdir = new Vector3(Input.GetAxisRaw("Horizontal") * right.x + Input.GetAxisRaw("Vertical") * forward.x , Mathf.Clamp(Input.GetAxisRaw("Horizontal") * right.y + Input.GetAxisRaw("Vertical") * forward.y + BoolToFloat(Input.GetButton("Jump")), -1f, 1f), Input.GetAxisRaw("Horizontal") * right.z + Input.GetAxisRaw("Vertical") * forward.z).normalized;

        //don't influence add_speed by making the normalized look down and let that component get lost later
        forward.y = 0f;
        right.y = 0f;

        //normalize, we only need directions
        forward.Normalize();
        right.Normalize();

        // Combine movement input
        Vector3 wishdir = (Input.GetAxisRaw("Horizontal") * right + Input.GetAxisRaw("Vertical") * forward).normalized;
        if (waterLevel > 1) {
            WaterAccelerate(waterWishdir);
        } else if (ground != null)
        {
            GroundAccelerate(wishdir, forward);
        }
        else
        {
            AirAccelerate(wishdir);
        }
    }

    void GroundAccelerate(Vector3 wishdir, Vector3 forward) 
    {
        
        Vector3 groundVel = Vector3.zero;

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

    void WaterAccelerate(Vector3 wishdir) //TODO: have parity, waterjump when wishdir flat into short ledge and jump down and almost out of water and recheck wishdir calculation above
    {
        if (Input.GetButton("Jump") && velocity.y < 100f) {
            velocity = new Vector3(velocity.x, 100f, velocity.z);
        }

        WaterFriction();

        if (velocity.magnitude <= 48f && wishdir.magnitude < 0.01) { //standing still sinks you
            velocity = new Vector3(velocity.x, Mathf.Clamp(velocity.y - 6f, -48f, velocity.y), velocity.z);
        }

        float currentSpeed = Vector3.Dot(velocity, wishdir);

        float wishSpeed = walkSpeed * classSpeedMod * swimSpeedMod;

        float addSpeed = Mathf.Clamp(wishSpeed - currentSpeed, 0f, waterAccel * wishSpeed * Time.fixedDeltaTime);

        Vector3 addVel = addSpeed * wishdir;

        if (Input.GetButton("Jump") && velocity.y > 100f  && addVel.y > 0f) {
            addVel = new Vector3(addVel.x, 0f, addVel.z);
        }

        velocity = velocity + addVel;
    }

    Vector3 GroundCheck(Vector3 pos, float checkDist)
    {
        Vector3 ret = pos;
        float groundYVel = 0f;

        if (velocity.y > leaveVelocity + groundYVel)
        {
            ground = null;
            return ret;
        }

        groundNormal = Vector3.up;
        
        GameObject found = null;
        float distance = 0f;
        float minDistance = checkDist;
        
        Vector3 origin = pos + Vector3.down * (isCrouching ? duckingBoundingBoxHeight : standingBoundingBoxHeight) / 2 +
        Vector3.up / 2;
        
        RaycastHit[] hits = Physics.BoxCastAll(
            origin,
            new Vector3(boundingBoxWidth, 1f, boundingBoxWidth) * 0.5f,
            Vector3.down,
            Quaternion.identity,
            checkDist,
            colliderMask,
            QueryTriggerInteraction.Ignore
        );

        
        foreach (RaycastHit hit in hits)
        {
            float dot = Vector3.Dot(hit.normal, Vector3.up);
            MeshCollider other = colliderResolver.ResolveCollider(hit.collider);
            if (hit.distance <= minDistance && tracer.GJKSweptIntersects(other, playerBounds() * 0.5f, pos, Vector3.down * checkDist)
                /*&& !tracer.GJKSweptIntersects(other, playerBounds() * 0.5f, pos, Vector3.down * 0f)*/) //crouch; getcomponent, all colliders are meshcolliders, this should be faster
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
        }

        if (ground != null && waterLevel < 2 && found != null)
        {
            ret = pos - Vector3.up * minDistance + Vector3.up * groundExtention;
        }

        ground = found;
        if (found != null) 
        {
            usedAirCrouches = 0;
        }
        return ret;
    }

    //collision is weird in source games, code to mimic traces as described in the collision section of "Review.pdf"
    //these are weird, so wallbugs (along other things) behave similiarly to the source/quake engine's bsp collision (TODO: actually copy it in more detail)
    bool UnsweptTrace(Vector3 pos)
    {
        Collider[] colliders = Physics.OverlapBox(
            pos,
            playerBounds() * 0.5f, //surfaceextention?, crouch?
            Quaternion.identity,
            colliderMask
        );
        for (int i = 0; i < colliders.Length; ++i) //check for intersects in GJK algorithm
        {
            if (tracer.GJKPointInside(colliderResolver.ResolveCollider(colliders[i]), playerBounds() * 0.5f, pos)) //crouch; getcomponent, all colliders are meshcolliders, this should be faster
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
            playerBounds() * 0.5f, //TODO: handle crouch; height multiplyer is weird, unity thing, temporary
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
            MeshCollider other = colliderResolver.ResolveCollider(hits[i].collider);
            if (tracer.GJKSweptIntersects(other, playerBounds() * 0.5f, startPos, wishmove.normalized * wishdist)) //crouch; getcomponent, all colliders are meshcolliders, this should be faster
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
                playerBounds() * 0.5f,
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
            RaycastHit backHit;
            float backCompleted = SweptTrace(startPos, wishpos, out backHit, out dummyObject); //this cannot be >= 1.0f, because then we wouldn't already be inside geometry
            if (backCompleted >= 1.0f) //not actually in geometry according to this check, shouldn't happen, also backHit.normal would be 0, so the surfaceextension would do nothing
            {
                return startPos + Max(new Vector3(0f, 0f, 0f), (wishpos - startPos) * backCompleted + surfaceExtention * backHit.normal, (wishpos - startPos).normalized);
            }
            if (backCompleted == 0f) //completely stuck, cannot move, for some reason this happens a lot when it shouldn't
            {
                velocity = Vector3.zero;
                return startPos;
            }
            //move enough to just leave geometry to get unstuck,
            return Move(depth - 1, timeLeft - (1.0f - backCompleted) * timeLeft, startPos + Max(new Vector3(0f, 0f, 0f), (wishpos - startPos) * (1.0f - backCompleted) + surfaceExtention * backHit.normal, (wishpos - startPos).normalized));
        }
        else
        {
            //velocity = velocity + Math.Abs(Vector3.Dot(velocity, hit.normal)) * hit.normal;
            velocity = velocity + remove;
        }
        //if collided wall, move playerup 18 units, forwards 0.04 units and stuckcheck+groundcheck to handle stairs
        //if (hit.distance < 1f)
        //{
            if (ground != null && waterLevel < 2)
            {
                //Debug.Log(dummyObject);
                Vector3 testPos = startPos + Max(new Vector3(0f, 0f, 0f), (wishpos - startPos) * completed + surfaceExtention * hit.normal, (wishpos - startPos).normalized) + new Vector3(0f, maxStepHeight + groundExtention * 2f, 0f) - hit.normal * minStepWidth;
                if (!UnsweptTrace(testPos)) //do not clip in walls
                {
                    GameObject oldGround = ground;
                    Vector3 oldNormal = groundNormal;
                    Vector3 stepped = GroundCheck(testPos, maxStepHeight + groundExtention * 3f); //stepped would ground us as we only check when grounded, this is the position we keep going
                    if (ground == null || oldGround == ground)
                    {
                        ground = oldGround;
                        groundNormal = oldNormal;
                    }
                    else
                    {   //if it was a stairsetp, recurse without decreasing velocity, so we don't just stop the movement after moving fast (decreasing depth, because otherwise stack overflow happens)
                        velocity = velocity - remove;
                        return Move(depth - 1, timeLeft - completed * timeLeft, stepped);
                    }
                }
            }
            else if (waterLevel == 2 && velocity.y > 99f && jumpLockOut >= 3) //try jumping out of water
            {
                jumpLockOut = 3;
                Vector3 testPos = startPos + Max(new Vector3(0f, 0f, 0f), (wishpos - startPos) * completed + surfaceExtention * hit.normal, (wishpos - startPos).normalized) + new Vector3(0f, 100f, 0f) - hit.normal * minStepWidth;
                if (!UnsweptTrace(testPos)) //do not attempt to jump out of water, and into a wall
                {
                    velocity = velocity + new Vector3(hit.normal.x, 0f, hit.normal.z) * 40f + Vector3.up * jumpVelocity;
                }
            }
        //}

        return Move(depth - 1, timeLeft - completed * timeLeft, startPos + Max(new Vector3(0f, 0f, 0f), (wishpos - startPos) * completed + surfaceExtention * hit.normal, (wishpos - startPos).normalized));
    }

    Vector3 playerBounds()
    {
        if (isCrouching)
        {
            return new Vector3(boundingBoxWidth, duckingBoundingBoxHeight, boundingBoxWidth);
        }
        return new Vector3(boundingBoxWidth, standingBoundingBoxHeight, boundingBoxWidth);
    }

    Vector3 HandleCrouch(Vector3 pos)
    {
        Vector3 ret = pos;
        bool crouchButtonState = Input.GetButton("Crouch");
        if (waterLevel == 3 || (ground == null && waterLevel > 0)) {
            crouchButtonState = false; //it seems to animate standing in tf2, probably calls -crouch
        }
        if (crouchButtonState) {
            if (!isCrouching) {
                if (ground == null) {
                    if (usedAirCrouches < maxAirCrouches) {
                        ret += new Vector3(0, 20, 0); //upshift to pull legs up instead of head down, this should make headbugs work, but unity doesn't check new collisions on this line, so I'll have to do it manually
                        Crouch(ref ret);
                    }
                }
                else if (currentCrouchFrame < crouchAnimLength && !isCrouching) { //make uncrouch anim fully finish
                    AnimateCrouch(ref ret);
                }
            } else if (ground != null && currentCrouchFrame > 0 && currentCrouchFrame < crouchAnimLength) {
                AnimateStand(ref ret);
            }
        } else {
            if (isCrouching || currentCrouchFrame > 0) { //ctap
                if (ground == null && Stand(ref ret)) {
                    ret -= new Vector3(0, 20, 0); //downshift to push legs down instead of head up
                }
            } if (ground != null && currentCrouchFrame > 0) {
                AnimateStand(ref ret);
            }
        }
        transform.localScale = playerBounds();
        return ret;
    }

    private bool Crouch(ref Vector3 pos) { //applies bounding box change on crouch
        if (usedAirCrouches >= maxAirCrouches || isCrouching) return false;
        ++usedAirCrouches;
        isCrouching = true;
        currentCrouchFrame = crouchAnimLength; //set to full crouch to not animate crouching on landing

        // shift collider down (unity scales to center, so by half of tf2's upshift, this also makes the camera stay in the correct spot)
        pos = pos + new Vector3(0f, -10f, 0f);
        //checkground -> shift up, but this doesn't happen on ctap, so idk
        return true;
    }

    private bool Stand(ref Vector3 pos) { //applies bounding box change on stand
        if (CanStand(pos)) {
            isCrouching = false;
            currentCrouchFrame = 0; //set to full stand to not animate standing up on landing
            pos = pos + new Vector3(0f, 10f, 0f);
            return true;
        }
        --usedAirCrouches; //this is here for ctap enjoyers
        if (!Crouch(ref pos)) //this is why the shift up isn't in the crouch function
        {
            ++usedAirCrouches;
        }
        return false; //for the shifting to be conditional (just spamming crouch while cannot iuncrouch shouldn't teleport you up)
    }

    private void AnimateCrouch(ref Vector3 pos) {
        if (currentCrouchFrame < crouchAnimLength)
        {
            currentCrouchFrame++;
        }
        if (currentCrouchFrame == crouchAnimLength)
        {
            Crouch(ref pos);
        }
    }

    private void AnimateStand(ref Vector3 pos) {
        if (!CanStand(pos)) return;
        if (currentCrouchFrame > 0)
        {
            currentCrouchFrame--;
        } 
        if (currentCrouchFrame == 0)
        {
            Stand(ref pos);
        }
    }

    bool CanStand(Vector3 pos) {
        Vector3 origin = pos + Vector3.down * 10f * (ground != null ? -1f : 1f);
        bool wasCrouching = isCrouching;
        isCrouching = false;
        bool cantStand = UnsweptTrace(origin);
        isCrouching = wasCrouching;
        return !cantStand;
    }

    //https://github.com/ValveSoftware/source-sdk-2013/blob/master/src/game/shared/tf/tf_gamemovement.cpp line 1452
    void EvaluateWater()
    {
        Vector3 vecPoint = new Vector3(transform.position.x, transform.position.y - playerBounds().y / 2f + 1f, transform.position.z); //check feet immersed in water first
        waterLevel = 0;
        if (CheckWaterAtPoint(vecPoint)) {
            waterLevel = 1;
            float flWaistY = transform.position.y + 12f; //tf2 has z for up and hungarian notation apparently floatWaistZ is flWaistZ
            vecPoint = new Vector3(transform.position.x, camY, transform.position.z);
            if (CheckWaterAtPoint(vecPoint)) {
                waterLevel = 3;
            } else {
                vecPoint = new Vector3(transform.position.x, flWaistY, transform.position.z);
                if (CheckWaterAtPoint(vecPoint)) {
                    waterLevel = 2;
                }
            }
        }
    }

    bool CheckWaterAtPoint(Vector3 point)
    {
        Collider[] colliders = Physics.OverlapBox(
            point,
            playerBounds() * 0.5f, //surfaceextention?, crouch?
            Quaternion.identity,
            waterMask
        );
        for (int i = 0; i < colliders.Length; ++i) //check for intersects in GJK algorithm
        {
            if (tracer.GJKPointInside(waterResolver.ResolveCollider(colliders[i]), new Vector3(0f, 0f, 0f), point))
            {
                return true;
            }
        }
        return false;
    }

    Vector3 HandleTriggers(Vector3 pos)
    {
        Vector3 ret = new Vector3(0f, pos.y, 0f);
        Collider[] colliders = Physics.OverlapBox(
            pos,
            playerBounds() * 0.5f, //surfaceextention?, crouch?
            Quaternion.identity,
            triggerMask
        );
        for (int i = 0; i < colliders.Length; ++i) //check for intersects in GJK algorithm
        {
            if (tracer.GJKPointInside(triggerResolver.ResolveCollider(colliders[i]), playerBounds() * 0.5f, pos))
            {
                return ret;
            }
        }
        return pos;
    }

    float BoolToFloat(bool b)
    {
        return b ? 1f : 0f;
    }

    Vector3 Max(Vector3 a, Vector3 b, Vector3 along)
    {
        return Vector3.Dot(along, a) > Vector3.Dot(along, b) ? a : b;
    }

    public bool GetCrouching()
    {
        return isCrouching;
    }
}