using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class TestBtn : MonoBehaviour
{
    public Button CleanUpButton;
    void Start()
    {
        CleanUpButton.onClick.AddListener(() => SceneManager.LoadScene(0));

    }

}
