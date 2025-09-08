using UnityEngine;
using System.Runtime.InteropServices;
using System;
using System.Collections;

public static class DFReaderWebGL
{
    private static int currentPage = 0;
    private static int totalPages = 0;
    private static bool isLoadingPage = false;
    private static string oldPath = string.Empty;
    private static int documentHandle = 0;
    private static int pageHandle = 0;
    private static int canvasHandle = 0;
    private static string currentPromiseHandle = "";
    private static Action<bool, int> documentCallback;
    private static Action<Texture2D, string> pageCallback;
#if UNITY_WEBGL && !UNITY_EDITOR

    [DllImport("__Internal")]
    private static extern void PDFJS_InitLibrary();

    [DllImport("__Internal")]
    private static extern void PDFJS_LoadDocumentFromURL(string promiseHandle, string url);

    [DllImport("__Internal")]
    private static extern void PDFJS_CloseDocument(int documentHandle);

    [DllImport("__Internal")]
    private static extern int PDFJS_GetPageCount(int documentHandle);

    [DllImport("__Internal")]
    private static extern void PDFJS_LoadPage(string promiseHandle, int documentHandle, int pageIndex);

    [DllImport("__Internal")]
    private static extern void PDFJS_ClosePage(int pageHandle);

    [DllImport("__Internal")]
    private static extern void PDFJS_RenderPageIntoCanvas(string promiseHandle, int pageHandle, float scale, int width, int height);

    [DllImport("__Internal")]
    private static extern void PDFJS_RenderCanvasIntoTexture(int canvasHandle, int textureHandle);

    [DllImport("__Internal")]
    private static extern void PDFJS_DestroyCanvas(int canvasHandle);

    [DllImport("__Internal")]
    private static extern float PDFJS_GetPageWidth(int pageHandle, float scale);

    [DllImport("__Internal")]
    private static extern float PDFJS_GetPageHeight(int pageHandle, float scale);
#endif

    public static void Initialize()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        PDFJS_InitLibrary();
#else
        Debug.LogError("PDFReaderWebGL is designed for WebGL only.");
#endif
    }

    // Called by PDFReaderWebGLCallbackHandler
    public static void OnLibraryInitialized()
    {
        Debug.Log("pdf.js initialized successfully.");
    }

    public static IEnumerator LoadPDFDocument(string url, Action<bool> onComplete)
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        Debug.LogError("PDFReaderWebGL is designed for WebGL only.");
        onComplete?.Invoke(false);
        yield break;
#else
        if (string.IsNullOrEmpty(url))
        {
            Debug.LogError("PDF URL is empty.");
            onComplete?.Invoke(false);
            yield break;
        }

        currentPromiseHandle = Guid.NewGuid().ToString();
        documentCallback = (success, pages) =>
        {
            if (success)
            {
                totalPages = pages;
                currentPage = 0;
                oldPath = url;
                Debug.Log($"Loaded PDF with {totalPages} pages from {url}.");
            }
            else
            {
                Debug.LogError("Failed to load PDF document.");
            }
            onComplete?.Invoke(success);
        };
        PDFJS_LoadDocumentFromURL(currentPromiseHandle, url);
        yield return new WaitUntil(() => documentCallback == null);
#endif
    }

    // Called by PDFReaderWebGLCallbackHandler
    public static void OnPromiseThen(string message)
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        return;
#else
        string[] parts = message.Split(new[] { "promiseHandle: ", " objectHandle: " }, StringSplitOptions.None);
        if (parts.Length < 3 || parts[1] != currentPromiseHandle) return;

        int objectHandle = int.Parse(parts[2]);
        if (documentCallback != null)
        {
            documentHandle = objectHandle;
            if (documentHandle > 0)
            {
                int pages = PDFJS_GetPageCount(documentHandle);
                documentCallback?.Invoke(true, pages);
            }
            else
            {
                documentCallback?.Invoke(false, 0);
            }
            documentCallback = null;
        }
        else if (pageCallback != null && objectHandle > 0)
        {
            if (pageHandle == 0)
            {
                pageHandle = objectHandle;
                float scale = 1200f / PDFJS_GetPageWidth(pageHandle, 1.0f);
                int width = Mathf.RoundToInt(PDFJS_GetPageWidth(pageHandle, scale));
                int height = Mathf.RoundToInt(PDFJS_GetPageHeight(pageHandle, scale));
                currentPromiseHandle = Guid.NewGuid().ToString();
                PDFJS_RenderPageIntoCanvas(currentPromiseHandle, pageHandle, scale, width, height);
            }
            else
            {
                canvasHandle = objectHandle;
                float scale = 1200f / PDFJS_GetPageWidth(pageHandle, 1.0f);
                int width = Mathf.RoundToInt(PDFJS_GetPageWidth(pageHandle, scale));
                int height = Mathf.RoundToInt(PDFJS_GetPageHeight(pageHandle, scale));
                Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                texture.filterMode = FilterMode.Bilinear;
                PDFJS_RenderCanvasIntoTexture(canvasHandle, texture.GetNativeTexturePtr().ToInt32());
                texture.Apply();
                pageCallback?.Invoke(texture, null);
                PDFJS_DestroyCanvas(canvasHandle);
                PDFJS_ClosePage(pageHandle);
                pageHandle = 0;
                canvasHandle = 0;
                pageCallback = null;
            }
        }
#endif
    }

    // Called by PDFReaderWebGLCallbackHandler
    public static void OnPromiseCatch(string message)
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        return;
#else
        string[] parts = message.Split(new[] { "promiseHandle: ", " objectHandle: " }, StringSplitOptions.None);
        if (parts.Length < 3 || parts[1] != currentPromiseHandle) return;

        Debug.LogError($"PDF operation failed: {message}");
        if (documentCallback != null)
        {
            documentCallback?.Invoke(false, 0);
            documentCallback = null;
        }
        else if (pageCallback != null)
        {
            pageCallback?.Invoke(null, "Error: Failed to load page");
            pageCallback = null;
        }
#endif
    }

    // Called by PDFReaderWebGLCallbackHandler
    public static void OnPromiseProgress(string message)
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        return;
#else
        Debug.Log($"PDF loading progress: {message}");
#endif
    }

    public static IEnumerator LoadPage(int pageIndex, Action<Texture2D, string> onComplete)
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        Debug.LogError("PDFReaderWebGL is designed for WebGL only.");
        onComplete?.Invoke(null, "Error: WebGL-only class");
        yield break;
#else
        string errorMessage = "";
        if (isLoadingPage)
        {
            errorMessage = "Another page is currently loading.";
            Debug.LogWarning(errorMessage);
            onComplete?.Invoke(null, errorMessage);
            yield break;
        }

        if (pageIndex < 0 || pageIndex >= totalPages)
        {
            errorMessage = $"Invalid page index: {pageIndex}";
            Debug.LogWarning(errorMessage);
            onComplete?.Invoke(null, errorMessage);
            yield break;
        }

        if (documentHandle == 0)
        {
            errorMessage = "No PDF document loaded.";
            Debug.LogWarning(errorMessage);
            onComplete?.Invoke(null, errorMessage);
            yield break;
        }

        isLoadingPage = true;
        currentPromiseHandle = Guid.NewGuid().ToString();
        pageCallback = (texture, error) =>
        {
            isLoadingPage = false;
            if (texture != null)
            {
                currentPage = pageIndex;
                Debug.Log($"Loaded page {pageIndex} successfully.");
            }
            onComplete?.Invoke(texture, error);
        };
        PDFJS_LoadPage(currentPromiseHandle, documentHandle, pageIndex + 1);
        yield return new WaitUntil(() => pageCallback == null);
#endif
    }

    public static void NextPage()
    {
        if (currentPage < totalPages - 1)
        {
            currentPage++;
            Debug.Log($"Switched to next page: {currentPage}");
        }
    }

    public static void PrevPage()
    {
        if (currentPage > 0)
        {
            currentPage--;
            Debug.Log($"Switched to previous page: {currentPage}");
        }
    }

    public static int GetCurrentPage()
    {
        return currentPage;
    }

    public static int GetTotalPages()
    {
        return totalPages;
    }

    public static void Cleanup()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        Debug.LogError("PDFReaderWebGL is designed for WebGL only.");
        return;
#else
        try
        {
            if (documentHandle > 0)
            {
                PDFJS_CloseDocument(documentHandle);
                documentHandle = 0;
                Debug.Log("Closed PDF document.");
            }
            if (pageHandle > 0)
            {
                PDFJS_ClosePage(pageHandle);
                pageHandle = 0;
                Debug.Log("Closed PDF page.");
            }
            if (canvasHandle > 0)
            {
                PDFJS_DestroyCanvas(canvasHandle);
                canvasHandle = 0;
                Debug.Log("Destroyed canvas.");
            }
            oldPath = string.Empty;
            totalPages = 0;
            currentPage = 0;
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed during cleanup: {e.Message}");
        }
#endif
    }
}