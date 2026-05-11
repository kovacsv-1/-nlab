using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    public MovementVariables playerVariables;

    private Vector3 velocity = new Vector3(0f, 0f, 0f);
    private GameObject ground = null;
    private GameObject dummyObject = null; //so SweptTrace doesn't need an insane amount of objects to be called 
    private Vector3 groundNormal = new Vector3(0f, 1f, 0f);
    private bool isCrouching = false;
    private bool jumping = false;
    private Transform cameraTf;
    private BoxCollider boundingBox;
    public LayerMask colliderMask;
    public LayerMask waterMask;
    public LayerMask triggerMask;

    private SweptTraces tracer;
    private GJKClosest gjkClosest;

    private int currentCrouchFrame = 0; // 0 -> fully standing; crouchAnimLength -> fully crouched
    private int usedAirCrouches = 0;
    private int usedMidAirJumps = 0;
    private int jumpLockOut = 0;

    private float camY = 0f;

    private int waterLevel = 0;

    public bool autoBHop = false;

    private bool jumpButtonPressed = false;
    private bool jumpButtonHeld = false;
    private bool crouchButtonHeld = false;
    private bool fire1ButtonHeld = false;
    private bool fire2ButtonHeld = false;
    private float horizontalInput = 0f;
    private float verticalInput = 0f;

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
        if (jumpLockOut < playerVariables.jumpLockOutLength)
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
            if (velocity.y > playerVariables.leaveVelocity) //TODO: rising platforms influence this
            {
                ground = null;
            }

            //  3. Handle ducking,
            newPos = HandleCrouch(newPos);

            //  4. Apply half of gravity,
            AddHalfGravity();

            //  5. Handle jumping,
            if (ground != null && waterLevel < 2 && jumping && !isCrouching && jumpLockOut >= playerVariables.jumpLockOutLength)
            {
                Jump();
            }
            else if (ground == null && waterLevel < 2 && jumping && jumpLockOut >= playerVariables.jumpLockOutLength && usedMidAirJumps < playerVariables.midAirJumps)
            {
                MidAirJump();
            }

            //  6. Cap velocity,
            CapSpeed(playerVariables.maxVelocity);

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
                newPos = GroundCheck(newPos, playerVariables.maxStepHeight + playerVariables.groundExtention * 1.1f); // * 1.079925477505f); //step down (yes, this is stupid, but I already have it implemented, so it should be fine)
            }
            //handle step up on collision, step down at the end of frame

            // 10. Check for ground to stand on,
            newPos = GroundCheck(newPos, playerVariables.groundingHeight);

            // 11. Apply other half of gravity,
            AddHalfGravity();

            // 12. If on ground, zero out vertical velocity,
            if (ground != null && waterLevel < 2)
            {
                velocity = velocity - Vector3.Dot(velocity, groundNormal) * groundNormal; //TODO: this works on horizontal planes, but will need to be changed when we add slopes, walking down on them would be jittery
            }

            // 13. Cap velocity,
            CapSpeed(playerVariables.maxVelocity);

        } //if stuck in step 1., skip to step 14.

        // 14. Check for triggers to activate,
        newPos = HandleTriggers(newPos); //todo make triggers have different effects

        // 15. Update bounding box,
        transform.position = newPos;
        camY = newPos.y - playerBounds().y / 2f + (playerVariables.standingViewHeight - (playerVariables.standingViewHeight - playerVariables.duckingViewHeight) * currentCrouchFrame / playerVariables.crouchAnimLength);
        cameraTf.position = new Vector3(newPos.x, camY, newPos.z);
        //transform.localScale = new Vector3(boundingBoxWidth, isCrouching ? duckingBoundingBoxHeight : standingBoundingBoxHeight, boundingBoxWidth);

        // 16. Shoot / detonate projectiles.

        jumping = false; //reset jump input, will be set to true again if the player presses the jump button before the next FixedUpdate
    }

    // Update is called once per frame
    void Update()
    {
        if (jumpButtonPressed || autoBHop && jumpButtonHeld)
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

        float control = Mathf.Max(speed, playerVariables.stopSpeed);
        float drop = control * playerVariables.friction * Time.fixedDeltaTime;

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
        float drop = control * playerVariables.waterFriction * Time.fixedDeltaTime;

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
        CapSpeed(playerVariables.walkSpeed * playerVariables.classSpeedMod * playerVariables.bhopCap);

        if (Vector3.Dot(groundNormal, Vector3.up) < 1f)
        {   //so we don't jump towards the slope every time we jump
            velocity = velocity + (playerVariables.gravity * 0.5f * Time.fixedDeltaTime) * groundNormal;
            velocity = new Vector3(velocity.x, 0f, velocity.z);
            velocity = velocity - (playerVariables.gravity * 0.5f * Time.fixedDeltaTime) * Vector3.up;
        }

        //  2. Unground the player,
        ground = null;
        groundNormal = Vector3.up;

        //  3. Apply jump velocity,
        if (currentCrouchFrame > 0 && currentCrouchFrame < playerVariables.crouchAnimLength)
        {
            velocity = new Vector3(velocity.x, playerVariables.jumpVelocity, velocity.z);
        }
        else 
        {
            velocity = new Vector3(velocity.x, velocity.y + playerVariables.jumpVelocity, velocity.z);
        }

        //  4. Apply half of gravity
        AddHalfGravity();
    }

    void MidAirJump()
    {
        Vector3 forward = cameraTf.forward;
        Vector3 right = cameraTf.right;
        forward.y = 0f;
        right.y = 0f;
        forward.Normalize();
        right.Normalize();
        Vector3 wishdir = (horizontalInput * right + verticalInput * forward).normalized;

        jumpLockOut = 0;
        //  1. don't Cap speed,

        //  2. Unground the player, redundant
        ground = null;
        groundNormal = Vector3.up;

        //  3. Apply jump velocity,
        switch (playerVariables.midAirJumpSetsVertical) 
        {
            case 0:
                velocity = velocity + Vector3.up * playerVariables.jumpVelocity;
                break;
            case 1:
                velocity = new Vector3(velocity.x, playerVariables.jumpVelocity, velocity.z);
                break;
            case 2:
                if (velocity.y <= 0)
                {
                    velocity = new Vector3(velocity.x, playerVariables.jumpVelocity, velocity.z);
                }
                else
                {
                    velocity = velocity + Vector3.up * playerVariables.jumpVelocity;
                }
                break;
            default:
                break;
        }

        float wishSpeed = 0f;

        switch (playerVariables.midAirJumpSetsHorizontal)
        {
            case 0:
                //does fuckall
                break;
            case 1:
                //this is what tf2 does and it is weird
                wishSpeed = playerVariables.walkSpeed * playerVariables.classSpeedMod * (isCrouching && playerVariables.airCrouchScalesSpeed ? playerVariables.duckSpeedMod : 1);
                velocity = new Vector3((wishSpeed * wishdir).x, velocity.y, (wishSpeed * wishdir).z);
                break;
            case 2:
                Vector3 horizontalVel = new Vector3(velocity.x, 0, velocity.z);
                wishSpeed = playerVariables.walkSpeed * playerVariables.classSpeedMod * (isCrouching && playerVariables.airCrouchScalesSpeed ? playerVariables.duckSpeedMod : 1);
                Vector3 addVel = (wishSpeed - Vector3.Dot(horizontalVel, wishdir)) * wishdir;
                velocity = addVel + velocity;
                break;
            default:
                break;
        }

        //  4. Apply half of gravity
        AddHalfGravity();

        ++usedMidAirJumps;
    }

    void AddHalfGravity()
    {
        if (waterLevel < 2)
        {
            velocity = velocity - (playerVariables.gravity * 0.5f * Time.fixedDeltaTime) * groundNormal; //TODO: if grounded on slopes this is weird, might slide down
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
        Vector3 waterWishdir = new Vector3(horizontalInput * right.x + verticalInput * forward.x , Mathf.Clamp(horizontalInput * right.y + verticalInput * forward.y + BoolToFloat(jumpButtonHeld), -1f, 1f), horizontalInput * right.z + verticalInput * forward.z).normalized;

        //don't influence add_speed by making the normalized look down and let that component get lost later
        forward.y = 0f;
        right.y = 0f;

        //normalize, we only need directions
        forward.Normalize();
        right.Normalize();

        // Combine movement input
        Vector3 wishdir = (horizontalInput * right + verticalInput * forward).normalized;
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

        if (playerVariables.hardSpeedCap && (velocity - groundVel).magnitude > playerVariables.walkSpeed * playerVariables.classSpeedMod) velocity = (velocity - groundVel).normalized * playerVariables.walkSpeed * playerVariables.classSpeedMod + groundVel; //not capspeed because groundmovement isn't a flat increase

        //weird ass code in quake, makes zigzagging and wallstrafing (as well as airstrafing, but different) work
        float currentSpeed = Vector3.Dot(velocity - groundVel, Vector3.ProjectOnPlane(wishdir, groundNormal).normalized);
        bool movingBackward = Vector3.Dot(wishdir, forward) < -0.5f;
        float wishSpeed = playerVariables.walkSpeed * playerVariables.classSpeedMod * (isCrouching ? playerVariables.duckSpeedMod : 1) * (movingBackward && !isCrouching ? playerVariables.backSpeedMod : 1);

        //this does way too much, but this is what clamp is for (the crouching and backwards check was added after this comment was already written, but it didn't stop me)
        float addSpeed = Mathf.Clamp(wishSpeed - currentSpeed, 0, playerVariables.groundAccel * wishSpeed * Time.fixedDeltaTime);

        //return new velocity
        velocity = velocity + addSpeed * Vector3.ProjectOnPlane(wishdir, groundNormal).normalized;
    }

    void AirAccelerate(Vector3 wishdir) 
    {
        //weird ass code in quake, makes airstrafing work
        float currentSpeed = Vector3.Dot(velocity, wishdir);
        float wishSpeed = playerVariables.walkSpeed * playerVariables.classSpeedMod * (isCrouching && playerVariables.airCrouchScalesSpeed ? playerVariables.duckSpeedMod : 1);

        //this does way too much, but this is what clamp is for
        float addSpeed = Mathf.Clamp(wishSpeed - currentSpeed, 0, playerVariables.airAccel * playerVariables.airSpeed * Time.fixedDeltaTime);

        //return new velocity
        velocity = velocity + addSpeed * wishdir;
        
    }

    void WaterAccelerate(Vector3 wishdir) //TODO: have parity, waterjump when wishdir flat into short ledge and jump down and almost out of water and recheck wishdir calculation above
    {
        if (jumpButtonHeld && velocity.y < 100f) {
            velocity = new Vector3(velocity.x, 100f, velocity.z);
        }

        WaterFriction();

        if (velocity.magnitude <= 48f && wishdir.magnitude < 0.01) { //standing still sinks you
            velocity = new Vector3(velocity.x, Mathf.Clamp(velocity.y - 6f, -48f, velocity.y), velocity.z);
        }

        float currentSpeed = Vector3.Dot(velocity, wishdir);

        float wishSpeed = playerVariables.walkSpeed * playerVariables.classSpeedMod * playerVariables.swimSpeedMod;

        float addSpeed = Mathf.Clamp(wishSpeed - currentSpeed, 0f, playerVariables.waterAccel * wishSpeed * Time.fixedDeltaTime);

        Vector3 addVel = addSpeed * wishdir;

        if (jumpButtonHeld && velocity.y > 100f  && addVel.y > 0f) {
            addVel = new Vector3(addVel.x, 0f, addVel.z);
        }

        velocity = velocity + addVel;
    }

    Vector3 GroundCheck(Vector3 pos, float checkDist)
    {
        Vector3 ret = pos;
        float groundYVel = 0f;

        if (velocity.y > playerVariables.leaveVelocity + groundYVel)
        {
            ground = null;
            return ret;
        }

        groundNormal = Vector3.up;
        
        GameObject found = null;
        float distance = 0f;
        float minDistance = checkDist;
        
        Vector3 origin = pos + Vector3.down * (isCrouching ? playerVariables.duckingBoundingBoxHeight : playerVariables.standingBoundingBoxHeight) / 2 +
        Vector3.up / 2;
        
        RaycastHit[] hits = Physics.BoxCastAll(
            origin,
            new Vector3(playerVariables.boundingBoxWidth, 1f, playerVariables.boundingBoxWidth) * 0.5f,
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
                if (dot >= playerVariables.cosMaxWalkableAngle)
                {
                    groundNormal = hit.normal;
                    found = hit.collider.gameObject;
                } 
            }
        }

        if (ground != null && waterLevel < 2 && found != null)
        {
            ret = pos - Vector3.up * minDistance + Vector3.up * playerVariables.groundExtention;
        }

        ground = found;
        if (found != null) 
        {
            usedAirCrouches = 0;
            usedMidAirJumps = 0;
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
            Debug.Log(hit.distance);
            Debug.Log(hit.point);
            Debug.Log(hit.normal);
            Debug.Log(collidedObject);
            Debug.Log(startPos);
            Debug.Log(velocity);
            return bestDistance / wishdist;
        }
        else
        {
            hit.normal = Vector3.zero;
            Debug.Log(hit.distance);
            Debug.Log(hit.point);
            Debug.Log(hit.normal);
            Debug.Log(collidedObject);
            Debug.Log(startPos);
            Debug.Log(velocity);
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
            return startPos + Max(new Vector3(0f, 0f, 0f), (wishpos - startPos) + playerVariables.surfaceExtention * hit.normal, (wishpos - startPos).normalized);
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
                return startPos + Max(new Vector3(0f, 0f, 0f), (wishpos - startPos) * backCompleted + playerVariables.surfaceExtention * backHit.normal, (wishpos - startPos).normalized);
            }
            if (backCompleted == 0f) //completely stuck, cannot move, for some reason this happens a lot when it shouldn't
            {
                velocity = Vector3.zero; //unswept trace does this better
                return startPos;
            }
            //move enough to just leave geometry to get unstuck,
            return Move(depth - 1, timeLeft - (1.0f - backCompleted) * timeLeft, startPos + Max(new Vector3(0f, 0f, 0f), (wishpos - startPos) * (1.0f - backCompleted) + playerVariables.surfaceExtention * backHit.normal, (wishpos - startPos).normalized));
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
                Vector3 testPos = startPos + Max(new Vector3(0f, 0f, 0f), (wishpos - startPos) * completed + playerVariables.surfaceExtention * hit.normal, (wishpos - startPos).normalized) + new Vector3(0f, playerVariables.maxStepHeight + playerVariables.groundExtention * 2f, 0f) - hit.normal * playerVariables.minStepWidth;
                if (!UnsweptTrace(testPos)) //do not clip in walls
                {
                    GameObject oldGround = ground;
                    Vector3 oldNormal = groundNormal;
                    testPos = startPos + Max(new Vector3(0f, 0f, 0f), (wishpos - startPos) * completed + playerVariables.surfaceExtention * hit.normal, (wishpos - startPos).normalized) + new Vector3(0f, playerVariables.maxStepHeight + playerVariables.groundExtention * 2f, 0f) - hit.normal * playerVariables.stepTeleDist;
                    Vector3 stepped = GroundCheck(testPos, playerVariables.maxStepHeight + playerVariables.groundExtention * 3f); //stepped would ground us as we only check when grounded, this is the position we keep going
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
            else if (waterLevel == 2 && velocity.y > 99f && jumpLockOut >= playerVariables.jumpLockOutLength) //try jumping out of water
            {
                jumpLockOut = playerVariables.jumpLockOutLength;
                Vector3 testPos = startPos + Max(new Vector3(0f, 0f, 0f), (wishpos - startPos) * completed + playerVariables.surfaceExtention * hit.normal, (wishpos - startPos).normalized) + new Vector3(0f, 100f, 0f) - hit.normal * playerVariables.minStepWidth;
                if (!UnsweptTrace(testPos)) //do not attempt to jump out of water, and into a wall
                {
                    velocity = velocity + new Vector3(hit.normal.x, 0f, hit.normal.z) * 40f + Vector3.up * playerVariables.jumpVelocity;
                }
            }
        //}

        return Move(depth - 1, timeLeft - completed * timeLeft, startPos + Max(new Vector3(0f, 0f, 0f), (wishpos - startPos) * completed + playerVariables.surfaceExtention * hit.normal, (wishpos - startPos).normalized));
    }

    Vector3 playerBounds()
    {
        if (isCrouching)
        {
            return new Vector3(playerVariables.boundingBoxWidth, playerVariables.duckingBoundingBoxHeight, playerVariables.boundingBoxWidth);
        }
        return new Vector3(playerVariables.boundingBoxWidth, playerVariables.standingBoundingBoxHeight, playerVariables.boundingBoxWidth);
    }

    Vector3 HandleCrouch(Vector3 pos)
    {
        Vector3 ret = pos;
        bool crouchButtonState = crouchButtonHeld;
        if (waterLevel == 3 || (ground == null && waterLevel > 0)) {
            crouchButtonState = false; //it seems to animate standing in tf2, probably calls -crouch
        }
        if (crouchButtonState) {
            if (!isCrouching) {
                if (ground == null) {
                    if (usedAirCrouches < playerVariables.maxAirCrouches) {
                        ret += new Vector3(0, 20, 0); //upshift to pull legs up instead of head down, this should make headbugs work, but unity doesn't check new collisions on this line, so I'll have to do it manually
                        Crouch(ref ret);
                    }
                }
                else if (currentCrouchFrame < playerVariables.crouchAnimLength && !isCrouching) { //make uncrouch anim fully finish
                    AnimateCrouch(ref ret);
                }
            } else if (ground != null && currentCrouchFrame > 0 && currentCrouchFrame < playerVariables.crouchAnimLength) {
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
        if (usedAirCrouches >= playerVariables.maxAirCrouches || isCrouching) return false;
        ++usedAirCrouches;
        isCrouching = true;
        currentCrouchFrame = playerVariables.crouchAnimLength; //set to full crouch to not animate crouching on landing

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
        if (currentCrouchFrame < playerVariables.crouchAnimLength)
        {
            currentCrouchFrame++;
        }
        if (currentCrouchFrame == playerVariables.crouchAnimLength)
        {
            Crouch(ref pos);
        }
    }

    private void AnimateStand(ref Vector3 pos) {
        if (!CanStand(pos)) {
            isCrouching = true;
            currentCrouchFrame = playerVariables.crouchAnimLength;
            return;
        }
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

    public void SetInputs(bool jumpDown, bool jumpHeld, bool crouchHeld, bool fire1Held, bool fire2Held, float horizontal, float vertical)
    {
        jumpButtonPressed = jumpDown;
        jumpButtonHeld = jumpHeld;
        crouchButtonHeld = crouchHeld;
        fire1ButtonHeld = fire1Held;
        fire2ButtonHeld = fire2Held;
        horizontalInput = horizontal;
        verticalInput = vertical;
    }
}