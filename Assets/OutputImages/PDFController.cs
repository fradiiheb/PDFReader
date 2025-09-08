using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class PDFController : MonoBehaviour
{
    public RawImage displayImage;
    public Button nextButton;
    public Button prevButton;
    public Button loadFrButton;
    public Button loadstreamButton;
    public Button cleanUpButton;

    public Text pageNumberText;
    [SerializeField] private string _pdfPath = "https://api.classquiz.tn/v2/exam/665dd8e7e1469_8_exam_13/extension/pdf/stream";
    [SerializeField] private string _pdfPathFr = "https://api.classquiz.tn/v2/exam/665dd8e8bed10_8_exam_19/extension/pdf/stream";
    [SerializeField] private string _pdfname = "stream.pdf";
    [SerializeField] private string _pdfnameFr = "FrExam.pdf";

    private string _currentDocumentPath = null; // Tracks loaded PDF path

    public Button zoomInButton;
    public Button zoomOutButton;

    [Tooltip("The amount the zoom changes with each button press.")]
    [SerializeField] private float zoomStep = 0.1f;

    [Tooltip("How fast the image zooms in and out.")]
    [SerializeField] private float zoomSpeed = 0.1f;

    [Tooltip("The minimum zoom level (e.g., 0.5 is 50% of original size).")]
    [SerializeField] private float minZoom = 0.5f;

    [Tooltip("The maximum zoom level (e.g., 3 is 300% of original size).")]
    [SerializeField] private float maxZoom = 3f;

    private Vector2 _originalSize;
    private float _currentZoom = 1f;

    public Image uiImage;
    public Button ConvertToImagebtn;
    private Texture2D nntexture;
    public Button Removebtn;

    void Start()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        PDFReaderWebGL.Initialize();
#else
        PDFReader.InitializePDFium();
#endif
        SetupUI();
        //_pdfPath = Application.streamingAssetsPath + "/stream.pdf";
        //LoadPDF(_pdfPath, _pdfname);
        _originalSize = displayImage.rectTransform.sizeDelta;
    }

    private void SetupUI()
    {
        nextButton.onClick.AddListener(() =>
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            PDFReaderWebGL.NextPage();
#else
            PDFReader.NextPage();
#endif
            StartCoroutine(LoadCurrentPage());
        });
        prevButton.onClick.AddListener(() =>
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            PDFReaderWebGL.PrevPage();
#else
            PDFReader.PrevPage();
#endif
            StartCoroutine(LoadCurrentPage());
        });
        loadFrButton.onClick.AddListener(() => LoadPDF(_pdfPathFr, _pdfnameFr));
        loadstreamButton.onClick.AddListener(() => LoadPDF(_pdfPath, _pdfname));
        cleanUpButton.onClick.AddListener(() => SceneManager.LoadScene(1));
        zoomInButton.onClick.AddListener(() => ZoomIn());
        zoomOutButton.onClick.AddListener(() => ZoomOut());
        ConvertToImagebtn.onClick.AddListener(() => TextureToSprite());
        Removebtn.onClick.AddListener(() => StartCoroutine(removeOld()));
    }

    private IEnumerator removeOld()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        var oldPath = Path.Combine(Application.persistentDataPath, _pdfnameFr);
        if (!string.IsNullOrEmpty(oldPath) && File.Exists(oldPath))
        {
            while (IsFileInUse(oldPath))
            {
                yield return new WaitForSeconds(0.1f); // Retry every 100ms
            }
            File.Delete(oldPath);
            Debug.Log("Old file deleted: " + oldPath);
        }
#else
        yield break; // No file deletion in WebGL
#endif
    }

    public static bool IsFileInUse(string filePath)
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        try
        {
            using (FileStream stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                return false;
            }
        }
        catch (IOException)
        {
            return true;
        }
#else
        return false; // WebGL doesn't use local files
#endif
    }

    private void LoadPDF(string fileUrl, string fileName)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        StartCoroutine(PDFReaderWebGL.LoadPDFDocument(fileUrl, (success) =>
        {
            _currentDocumentPath = fileUrl;
            if (success)
            {
                StartCoroutine(LoadCurrentPage());
            }
            else
            {
                pageNumberText.text = "Error: Failed to load document";
            }
        }));
#else
        StartCoroutine(PDFReader.DownloadAndLoadPDF(fileUrl,
            Path.Combine(Application.persistentDataPath, fileName),
            (path) =>
            {
                _currentDocumentPath = Path.Combine(Application.persistentDataPath, fileName);
                if (PDFReader.LoadPDFDocument(path))
                {
                    StartCoroutine(LoadCurrentPage());
                    Debug.Log($"Loaded PDF from {path}");
                }
                else
                {
                    pageNumberText.text = "Error: Failed to load document";
                }
            },
            (error) => pageNumberText.text = error));
#endif
    }

    private IEnumerator LoadCurrentPage()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        yield return PDFReaderWebGL.LoadPage(PDFReaderWebGL.GetCurrentPage(), (texture, errorMessage) =>
        {
            if (texture != null)
            {
                if (displayImage.texture != null)
                {
                    Destroy(displayImage.texture);
                }
                displayImage.texture = texture;
                nntexture = texture;
                pageNumberText.text = $"Page {PDFReaderWebGL.GetCurrentPage() + 1}/{PDFReaderWebGL.GetTotalPages()}";
            }
            else
            {
                pageNumberText.text = errorMessage;
            }
        });
#else
        Texture2D texture = PDFReader.LoadPage(PDFReader.GetCurrentPage(), out var errorMessage);
        if (texture != null)
        {
            if (displayImage.texture != null)
            {
                Destroy(displayImage.texture);
            }
            displayImage.texture = texture;
            nntexture = texture;
            pageNumberText.text = $"Page {PDFReader.GetCurrentPage() + 1}/{PDFReader.GetTotalPages()}";
        }
        else
        {
            pageNumberText.text = errorMessage;
        }
        yield return null;
#endif
    }

    private void TextureToSprite()
    {
        if (nntexture == null)
        {
            Debug.LogWarning("No texture available to convert to sprite.");
            return;
        }
        Sprite sprite = Sprite.Create(
            nntexture,
            new Rect(0, 0, nntexture.width, nntexture.height),
            new Vector2(0.5f, 0.5f),
            100.0f
        );
        uiImage.sprite = sprite;
        displayImage.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        PDFReaderWebGL.Cleanup();
#else
        PDFReader.Cleanup();
#endif
    }

    void OnApplicationQuit()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        PDFReaderWebGL.Cleanup();
#else
        PDFReader.Cleanup();
#endif
    }

    private void ZoomIn()
    {
        _currentZoom += zoomStep;
        _currentZoom = Mathf.Clamp(_currentZoom, minZoom, maxZoom);
        ApplyZoom();
    }

    private void ZoomOut()
    {
        _currentZoom -= zoomStep;
        _currentZoom = Mathf.Clamp(_currentZoom, minZoom, maxZoom);
        ApplyZoom();
    }

    private void ApplyZoom()
    {
        Vector2 newSize = _originalSize * _currentZoom;
        displayImage.rectTransform.sizeDelta = newSize;
    }

    public void SetZoom(float zoomLevel)
    {
        _currentZoom = Mathf.Clamp(zoomLevel, minZoom, maxZoom);
        ApplyZoom();
    }
}