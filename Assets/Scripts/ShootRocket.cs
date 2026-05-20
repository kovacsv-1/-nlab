using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class ShootRocket : MonoBehaviour
{
    public Transform cam;
    public GameObject rocket;
    public GameObject player;
    public PlayerMovement playerMovement;
    public float attackTime = 0.8f;
    private bool canFire = true;

    private bool wantsToFire = false;
    
    public LayerMask lockOn = 641; //enemy players, map geometry, projectiles shot by others, anything the player can collide with

    //void FixedUpdate() {
    //    if (wantsToFire && canFire) StartCoroutine(fire());
    //}

    IEnumerator fire() {
        canFire = false;
        GameObject newRocket = Instantiate(rocket, cam.position, Quaternion.identity);
        RocketScript rs = newRocket.GetComponent<RocketScript>();
        rs.owner = player;
        rs.applyOffset(cam);
        Vector3 dir = (cam.transform.position + cam.forward * 2000f - rs.selfTransform.position).normalized; //add deviation to this for beggars
        RaycastHit hit;
        if (Physics.Raycast(cam.transform.position, cam.forward, out hit, 2000f, lockOn)) //true if hit something, ignores player as the ray's origin is within the player's collision box; TODO: replace with gjk based raycast (also in rocketscript)
        { 
            if (hit.distance >= 200f) {
                dir = (cam.transform.position + cam.forward * hit.distance - rs.selfTransform.position).normalized; //tf2's aim assist
            }
        }
        rs.setDir(dir);
        player.GetComponent<AudioSource>().Play();
        //TODO: add the rocket to player's rockets to handle step from movement
        playerMovement.AddRocket(newRocket);
        yield return new WaitForSeconds(attackTime);
        canFire = true;
    }

    //public void SetInputs(bool fire1)
    //{
    //    wantsToFire = fire1;
    //}

    public void Shoot()
    {
        if (canFire) StartCoroutine(fire());
    }
}
