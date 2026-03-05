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

    List<Vector3> poss = new List<Vector3>();
    List<Vector3> dirst = new List<Vector3>();

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        cameraTf = GetComponentInChildren<Camera>().GetComponent<Transform>();
        boundingBox = GetComponent<BoxCollider>();
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
        if (colliders.Length > 0)
        {
            Debug.Log(colliders[0].gameObject);
            return true;
        }
        return false;
    }

    //this is weird, has 2 version for leaves and brushes respectively: a <= b and a - b <= 0, so there are some rounding disagreements, causing bugs, which, for now, I will not replicate
    float SweptTrace(Vector3 startPos, Vector3 wishPos, out RaycastHit hit)
    {
        Vector3 wishmove = wishPos - startPos;
        float wishdist = wishmove.magnitude;
        Vector3 wishdir = new Vector3(wishmove.x, wishmove.y, wishmove.z).normalized;
        float ret = 1.0f;
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

        //do axis aligned bounding box maths for better collision, currently doing some unnecessary raycast hacks, that do not work
        List<RaycastHit> trueHits = new List<RaycastHit>();

        for (int i = 0; i < hits.Length; ++i)
        {
            Vector3 otherPoint;
            Vector3 thisPoint; //raycast from here towards wishdir to get true distance and contactpoint
            Vector3 colPoint;
            //computepenetration methods

            FindContactPoint(hits[i].collider, startPos, hits[i].distance * wishdir, out otherPoint, out thisPoint); //uses ClosesPoint to find the points on 2 colliders closest to each other
            //this is some precise shit
            //if mario64 can afford 3 raycasts per frame for the shadow rendering, I can afford one to have perfect collisions
            if (CheckLineBoxIntersection(otherPoint, -wishdir, startPos, new Vector3(boundingBoxWidth, standingBoundingBoxHeight, boundingBoxWidth), out colPoint)) //TODO: crouch; finds if contact is true or just unity's offset
            {
                float dist = (otherPoint - colPoint).magnitude;
                RaycastHit route = new RaycastHit();
                route.distance = dist;
                route.point = otherPoint;
                route.normal = GetNormalAtPoint(hits[i].collider, otherPoint);
                trueHits.Add(route);
            }
        }
        trueHits.Sort((a, b) => a.distance.CompareTo(b.distance));

        if (trueHits.Count > 0)
        {
            hit = trueHits[0];
            ret = hit.distance / wishdist;
        }

        return ret;
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

    float FindContactPoint(Collider other, Vector3 startPos, Vector3 offset, out Vector3 otherPoint, out Vector3 thisPoint)
    {
        //costly as fuck
        BoxCollider test = gameObject.AddComponent<BoxCollider>();
        test.center = new Vector3(offset.x / boundingBoxWidth, offset.y / standingBoundingBoxHeight, offset.z / boundingBoxWidth); //this scales with localscale, lmao; TODO: handle crouching
        Vector3 otherPoint1 = other.ClosestPoint(startPos + offset); //ClosestPointOnBounds / ClosestPoint
        Vector3 thisPoint1 = test.ClosestPoint(other.gameObject.transform.position);
        Vector3 otherPoint2 = other.ClosestPoint(thisPoint1);
        Vector3 thisPoint2 = test.ClosestPoint(otherPoint1);
        Vector3 interPoint;
        if (CheckLineIntersection(otherPoint1, otherPoint2, thisPoint1, thisPoint2, out interPoint) >= 0f)
        {
            otherPoint = other.ClosestPoint(interPoint);
            thisPoint = test.ClosestPoint(interPoint);
        }
        else
        {
            otherPoint = other.ClosestPoint(thisPoint1 + (thisPoint2 - thisPoint1) / 2f);
            thisPoint = test.ClosestPoint(otherPoint1 + (otherPoint2 - otherPoint1) / 2f);
        }
        Destroy(test);

        return Vector3.Distance(thisPoint, otherPoint);
    }

    bool CheckLineBoxIntersection(Vector3 point, Vector3 direction, Vector3 boxCenter, Vector3 boxDimensions, out Vector3 intersectionPoint)
    {
        Vector3 halfDimensions = boxDimensions / 2;

        Vector3 boxMin = boxCenter - halfDimensions;
        Vector3 boxMax = boxCenter + halfDimensions;

        float tMinX = (boxMin.x - point.x) / direction.x;
        float tMaxX = (boxMax.x - point.x) / direction.x;

        float tMinY = (boxMin.y - point.y) / direction.y;
        float tMaxY = (boxMax.y - point.y) / direction.y;

        float tMinZ = (boxMin.z - point.z) / direction.z;
        float tMaxZ = (boxMax.z - point.z) / direction.z;

        float entryT = Math.Max(Math.Max(Math.Min(tMinX, tMaxX), Math.Min(tMinY, tMaxY)), Math.Min(tMinZ, tMaxZ));
        float exitT = Math.Min(Math.Min(Math.Max(tMinX, tMaxX), Math.Max(tMinY, tMaxY)), Math.Max(tMinZ, tMaxZ));

        if (entryT <= exitT && exitT >= 0)
        {
            intersectionPoint = point + entryT * direction;
            return true;
        }

        intersectionPoint = Vector3.zero; 
        return false;
    }

    Vector3 GetNormalAtPoint(Collider collider, Vector3 contactPoint) //works sometimes for non rotated stuff, but has side priority as the player is not a point, might need to use some odd offsets
    {
        Quaternion colliderRot = collider.gameObject.transform.rotation;
        if (collider is MeshCollider meshCollider && !meshCollider.convex)
        {
            Vector3[] vertices = meshCollider.sharedMesh.vertices;
            Vector3[] normals = meshCollider.sharedMesh.normals;

            Vector3 localContactPoint = meshCollider.transform.InverseTransformPoint(contactPoint);

            Vector3 nearest = Vector3.zero;
            float shortestDistance = float.MaxValue;
            int nearestIndex = -1;

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 worldVertex = meshCollider.transform.TransformPoint(vertices[i]);
                float distance = Vector3.Distance(worldVertex, contactPoint);

                if (distance < shortestDistance)
                {
                    shortestDistance = distance;
                    nearest = worldVertex;
                    nearestIndex = i;
                }
            }

            return (nearestIndex >= 0) ? normals[nearestIndex].normalized : Vector3.up;
        }
        else if (collider is BoxCollider boxCollider) //TODO: edges and problem when y is involves, due to not being a cube
        {
            Vector3 localPoint = boxCollider.transform.InverseTransformPoint(contactPoint);
            //localPoint = Quaternion.Inverse(colliderRot) * localPoint;

            float absoluteX = Mathf.Abs(localPoint.x - boxCollider.center.x);
            float absoluteY = Mathf.Abs(localPoint.y - boxCollider.center.y);
            float absoluteZ = Mathf.Abs(localPoint.z - boxCollider.center.z);

            Vector3 ret = Vector3.zero;

            if (absoluteX >= absoluteY && absoluteX >= absoluteZ)
            {
                ret += new Vector3(Mathf.Sign(localPoint.x), 0, 0);
            }
            if (absoluteY >= absoluteZ && absoluteY >= absoluteX)
            {
                ret += new Vector3(0, Mathf.Sign(localPoint.y), 0);
            }
            if (absoluteZ >= absoluteY && absoluteZ >= absoluteX)
            {
                ret += new Vector3(0, 0, Mathf.Sign(localPoint.z));
            }
            ret = colliderRot * ret;
            return ret.normalized;
        }
        else if (collider is SphereCollider sphereCollider)
        {
            Vector3 centerToPoint = contactPoint - sphereCollider.transform.position;
            return centerToPoint.normalized;
        }
        else if (collider is CapsuleCollider capsuleCollider) //this only works for upright ones
        {
            Vector3 halfHeight = capsuleCollider.height / 2 * Vector3.up;
            Vector3 bottom = capsuleCollider.transform.position - halfHeight;
            Vector3 top = capsuleCollider.transform.position + halfHeight;

            Vector3 closestPointOnCapsule = Vector3.Lerp(bottom, top, Mathf.Clamp01(Vector3.Dot(contactPoint - bottom, top - bottom) / capsuleCollider.height));
            Vector3 direction = contactPoint - closestPointOnCapsule;

            return direction.normalized;
        }

        return Vector3.up;
    }

    float CheckLineIntersection(Vector3 p1, Vector3 p2, Vector3 q1, Vector3 q2, out Vector3 interPoint)
    {
        const float EPS = 1e-6f;

        Vector3 d1 = p2 - p1;
        Vector3 d2 = q2 - q1;
        Vector3 r = p1 - q1;

        float a = Vector3.Dot(d1, d1);
        float e = Vector3.Dot(d2, d2);
        float f = Vector3.Dot(d2, r);

        float s, t;

        if (Vector3.Cross(d1, d2).sqrMagnitude < EPS)
        {
            interPoint = Vector3.zero;
            return -1f;
        }

        if (a <= EPS && e <= EPS)
        {
            interPoint = (p1 + q1) * 0.5f;
            return Vector3.Distance(p1, q1);
        }

        if (a <= EPS)
        {
            s = 0f;
            t = Mathf.Clamp01(f / e);
        }
        else
        {
            float c = Vector3.Dot(d1, r);

            if (e <= EPS)
            {
                t = 0f;
                s = Mathf.Clamp01(-c / a);
            }
            else
            {
                float b = Vector3.Dot(d1, d2);
                float denom = a * e - b * b;

                if (denom != 0f)
                    s = Mathf.Clamp01((b * f - c * e) / denom);
                else
                    s = 0f;

                float tNom = (b * s + f);

                if (tNom < 0f)
                {
                    t = 0f;
                    s = Mathf.Clamp01(-c / a);
                }
                else if (tNom > e)
                {
                    t = 1f;
                    s = Mathf.Clamp01((b - c) / a);
                }
                else
                {
                    t = tNom / e;
                }
            }
        }

        Vector3 closestP = p1 + d1 * s;
        Vector3 closestQ = q1 + d2 * t;

        interPoint = (closestP + closestQ) * 0.5f;

        return Vector3.Distance(closestP, closestQ);
    }
}
