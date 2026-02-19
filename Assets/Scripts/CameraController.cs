using UnityEngine;

public class CameraController : MonoBehaviour
{
    public float mouseSensitivity = 7.5f;

    public float xRotation = 0f; // Pitch
    public float yRotation = 0f; // Yaw

    //keys to achieve consistent turn speed, (should have a modifyer key to toggle between 2 turn speeds)
    //public float keySensitivity = 0.1f;
    //private bool q = false;
    //private bool e = false;

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        mouseSensitivity = PlayerPrefs.GetFloat("MouseSensitivity", 7.5f); //loading from my other project
        //keySensitivity = PlayerPrefs.GetFloat("TurnSpeed", 1f); //loading from my other project
    }

    //void Update is smoother, but I don't want the camera and movement to desync, if interpolation works in movement, replace
    void LateUpdate()
    {
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        //if (!q) q = Input.GetButton("Q");
        //if (!e) e = Input.GetButton("E");

        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -89f, 89f); // Prevent flipping over

        yRotation += mouseX;

        //originally in fixedupdate
        //if (e) yRotation += keySensitivity;
        //if (q) yRotation -= keySensitivity;
        //e = false;
        //q = false;

        transform.rotation = Quaternion.Euler(xRotation, yRotation, 0f);
    }

    /*
    moved to lateupdate
    void FixedUpdate()
    {
        if (e) yRotation += keySensitivity;
        if (q) yRotation -= keySensitivity;
        e = false;
        q = false;
    }*/

    public void SetSensitivity(float f)
    {
        mouseSensitivity = f;
    }

    /*public void SetKeySensitivity(float f)
    {
        keySensitivity = f;
    }*/
}
