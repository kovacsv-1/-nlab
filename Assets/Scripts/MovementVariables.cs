using UnityEngine;

[CreateAssetMenu(fileName = "MovementVariables", menuName = "Scriptable Objects/MovementVariables")]
public class MovementVariables : ScriptableObject
{
    //values taken from tf2:
    //tickrate: 66.6...; a tick is 0.015 seconds
    public float surfaceExtention = 0.03125f;
    public float groundExtention = 0.705f;
    public float groundingHeight = 2f;
    public float maxVelocity = 3500f;
    public float leaveVelocity = 250f;
    public float cosMaxWalkableAngle = 0.7f; //cos(45.58)
    public float maxStepHeight = 18f;
    public float minStepWidth = 0.04f;
    public float stepTeleDist = 0.04f;
    public float gravity = 800f;
    public float jumpVelocity = 289f;
    public float bhopCap = 1.2f; //tf2 reduces your velocity to your walkspeed * 1.2 after a jump to nerf bhopping
    public float walkSpeed = 300f;
    public float airSpeed = 30f;
    public float friction = 4f; //friction for regular ground, idk what ice uses, or if there even are ice physics in the engine
    public float waterFriction = 1f;
    public float stopSpeed = 100f; //the speed at which friction will stop you, if your velocity is less than this, it will be set to 0 instead of being reduced by friction
    public float duckSpeedMod = 1f / 3f; //tf2 reduces your max walking speed to 1/3 of your normal speed when ducking, cs:source also does this in the air
    public bool airCrouchScalesSpeed = false;
    public float backSpeedMod = 0.9f; //tf2 reduces your max walking speed to 90% when walking backwards
    public float swimSpeedMod = 0.8f;
    public float classSpeedMod = 1f; //1.33 for scout; 1 for pyro,engie,sniper; 0.8 for soldier; 0.93 for demo; 0.77 for heavy; 1.07 for medic,spy
    public int jumpLockOutLength = 3; //so autobhop doesn't make us jump twice in one frame for some reason
    public float groundAccel = 10f; //reach max speed in 0.1s
    public float airAccel = 10f;
    public float waterAccel = 10f;
    public int maxAirCrouches = 2; //this is tf2 exclusive in the source engine
    public int crouchAnimLength = 10; // 6/66.6...s to crouch/uncrouch 
    public bool hardSpeedCap = true;
    public float boundingBoxWidth = 48f; // or 49
    public float standingBoundingBoxHeight = 82f; // or 83
    public float duckingBoundingBoxHeight = 62f; // or 63
    public float oldDuckingBoundingBoxHeight = 55f;
    public int midAirJumps = 0;
    public int midAirJumpSetsVertical = 1; //0 midair jump just adds jumpvel to y; 1 it sets y to jumpvel; 2 it sets it if y <= 0 otherwise adds
    public int midAirJumpSetsHorizontal = 1; //0 midair jump doesn't overwrite airaccel; 1 it sets current horizontal speed towards wishdir at wishspeed; 2 it sets it if dot product of current and wishspeed is negative otherwise adds to it
    public bool autoBHop = false;

    //idk, dude, this game is weird: https://www.youtube.com/watch?v=AUPBC5W1KHo
    public float minAimAssistDist = 200f;
    public float maxAimAssistDist = 2000f;
    //projectile spawning location offsets from camera when firing
    /*
    public float forwardProjectileOffset = 23.5f;

    public float standingUpwardProjectileOffset = -3f;
    public float duckingUpwardProjectileOffset = 8f;

    public float stockRightwardProjectileOffset = 15f;
    public float cowManglerRightwardProjectileOffset = 8f; 
    public float originalRightwardProjectileOffset = 0f;
    */
    public Vector3 standingProjectileOffset = new Vector3(23.5f, -3f, 15f);
    public Vector3 crouchingProjectileOffset = new Vector3(23.5f, 8f, 15f);
    public bool leftHanded = false;
    public float knockbackMod = 10f; //9 for demo
    public float knockbackGroundMod = 5f; //9 for demo
    public float selfDamageMod = 1f; //0.75f for demo
    public float selfDamageAirMod = 0.6f; //0.75f for demo

    public float standingViewHeight = 68f; // 65 for scout, 75 for heavy and support classes
    public float duckingViewHeight = 45f;
}
