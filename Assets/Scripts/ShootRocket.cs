using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static UnityEngine.UI.GridLayoutGroup;

public class ShootRocket : MonoBehaviour
{
    public Transform cam;
    public GameObject rocket;
    public GameObject player;
    public PlayerMovement playerMovement;
    public RocketVariables rocketVariables;
    public float attackTime = 0.8f;
    private bool canFire = true;
    
    public LayerMask lockOn = 641; //enemy players, map geometry, projectiles shot by others, anything the player can collide with

    IEnumerator fire() {
        canFire = false;
        GameObject newRocket = Instantiate(rocket, cam.position, Quaternion.identity);
        RocketScript rs = newRocket.GetComponent<RocketScript>();
        rs.owner = player;
        rs.playerMovement = playerMovement;
        Vector3 offset = Vector3.zero;
        float forwardOffset = playerMovement.GetCrouching() ? rocketVariables.crouchingProjectileOffset.x : rocketVariables.standingProjectileOffset.x;
        offset += forwardOffset * cam.forward;
        float rightOffset = playerMovement.GetCrouching() ? rocketVariables.crouchingProjectileOffset.z : rocketVariables.standingProjectileOffset.z;
        offset += rightOffset * cam.right * (rocketVariables.leftHanded ? -1f : 1f);
        float upOffset = playerMovement.GetCrouching() ? rocketVariables.crouchingProjectileOffset.y : rocketVariables.standingProjectileOffset.y;
        offset += upOffset * cam.up;
        rs.applyOffset(offset);
        rs.SetRocketSpeed(rocketVariables.rocketSpeed);
        Vector3 dir = (cam.transform.position + cam.forward * rocketVariables.maxAimAssistDist - rs.selfTransform.position).normalized; //add deviation to this for beggars
        RaycastHit hit;
        if (Physics.Raycast(cam.transform.position, cam.forward, out hit, rocketVariables.maxAimAssistDist, lockOn)) //true if hit something, ignores player as the ray's origin is within the player's collision box; TODO: replace with gjk based raycast (also in rocketscript)
        { 
            if (hit.distance >= rocketVariables.minAimAssistDist) {
                dir = (cam.transform.position + cam.forward * hit.distance - rs.selfTransform.position).normalized; //tf2's aim assist
            }
        }
        rs.setDir(dir);
        player.GetComponent<AudioSource>().Play();
        playerMovement.AddRocket(newRocket);
        yield return new WaitForSeconds(attackTime);
        canFire = true;
    }

    public void Shoot()
    {
        if (canFire) StartCoroutine(fire());
    }
}
