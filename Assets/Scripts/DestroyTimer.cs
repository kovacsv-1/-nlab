using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class DestroyTimer : MonoBehaviour
{
    public float sec = 2f;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        StartCoroutine(del());
    }

    IEnumerator del() {
        yield return new WaitForSeconds(sec);
        Destroy(this.gameObject);
    }
}
