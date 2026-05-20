using UnityEngine;

public class InputHandling : MonoBehaviour
{
    public PlayerMovement movement;
    //public ShootRocket firing;

    // Update is called once per frame
    void Update()
    {
        movement.SetInputs(Input.GetButtonDown("Jump"), Input.GetButton("Jump"), Input.GetButton("Crouch"), Input.GetButton("Fire1"), Input.GetButton("Fire2"), Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));       
        //firing.SetInputs(Input.GetButton("Fire1"));
    }
}
