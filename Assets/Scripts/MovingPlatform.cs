using UnityEngine;

public class MovingPlatform : MonoBehaviour
{
    public Vector3[] velocities;
    public int[] timings;
    private int current = 0;
    private int currentFrame = 0;
    public bool started = true;
    public bool looping = true;
    private bool wasWalking = false;
    private Vector3 startPos;

    void Awake() {
        startPos = this.transform.position;
        for (int i = 0; i < timings.Length; ++i)
            timings[i] /= 2;
    }

    void FixedUpdate() {
        if (!started) return;
        if (currentFrame < timings[current]) {
            Vector3 movement = velocities[current] * Time.fixedDeltaTime;
            transform.position += movement;
            currentFrame++;
        } else {
            currentFrame = 0;
            current = (current + 1) % velocities.Length;
            if (current == 0 && !looping) {
                transform.position = startPos;
                started = false;
            }
        }
    }

    public Vector3 GetCurrentVelocity() {
        if (!started) return Vector3.zero;
        return velocities[current];
    }

    public void StartMovement() {
        started = true;
    }

    public void StopMovement() {
        started = false;
    }
}
