using UnityEngine;

public class PlayerInput : MonoBehaviour
{
    public static float Horizontal;
    public static bool Jump;

    void Update()
    {
        Horizontal = Input.GetAxis("Horizontal");
        Jump = Input.GetButtonDown("Jump");
    }
}
