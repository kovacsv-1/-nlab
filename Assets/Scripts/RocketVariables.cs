using UnityEngine;

[CreateAssetMenu(fileName = "RocketVariables", menuName = "Scriptable Objects/RocketVariables")]
public class RocketVariables : ScriptableObject
{

    public float rocketSpeed = 1100f;
    //idk, dude, this game is weird: https://www.youtube.com/watch?v=AUPBC5W1KHo
    public float minAimAssistDist = 200f;
    public float maxAimAssistDist = 2000f;
    //projectile spawning locationoffsets from camera when firing
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
}
