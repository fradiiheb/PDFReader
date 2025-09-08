using UnityEngine;

public class PDFReaderWebGLCallbackHandler : MonoBehaviour
{
#if UNITY_WEBGL && !UNITY_EDITOR
    void OnLibraryInitialized(string message)
    {
        PDFReaderWebGL.OnLibraryInitialized();
    }

    void OnPromiseThen(string message)
    {
        PDFReaderWebGL.OnPromiseThen(message);
    }

    void OnPromiseCatch(string message)
    {
        PDFReaderWebGL.OnPromiseCatch(message);
    }

    void OnPromiseProgress(string message)
    {
        PDFReaderWebGL.OnPromiseProgress(message);
    }
#endif
}