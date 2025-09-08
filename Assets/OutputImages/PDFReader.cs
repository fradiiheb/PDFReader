using UnityEngine;
using System.Runtime.InteropServices;
using System;
using System.IO;
using UnityEngine.Networking;

public static class PDFReader
{
    // PDFium variables
    private static IntPtr document = IntPtr.Zero;
    private static IntPtr page = IntPtr.Zero;
    private static int currentPage = 0;
    private static int totalPages = 0;
    private static bool isLoadingPage = false;
    private static string oldPath = string.Empty;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    private const string pdfiumLib = "pdfium.dll";
#elif UNITY_ANDROID
    private const string pdfiumLib = "libpdfium";
#elif UNITY_IOS
    private const string pdfiumLib = "__Internal";
#else
    private const string pdfiumLib = "pdfium";
#endif

    // PDFium function imports
    [DllImport(pdfiumLib)]
    private static extern void FPDF_InitLibrary();

    [DllImport(pdfiumLib)]
    private static extern void FPDF_DestroyLibrary();

    [DllImport(pdfiumLib)]
    private static extern IntPtr FPDF_LoadDocument(string file_path, string password);

    [DllImport(pdfiumLib)]
    private static extern void FPDF_CloseDocument(IntPtr document);

    [DllImport(pdfiumLib)]
    private static extern int FPDF_GetPageCount(IntPtr document);

    [DllImport(pdfiumLib)]
    private static extern IntPtr FPDF_LoadPage(IntPtr document, int page_index);

    [DllImport(pdfiumLib)]
    private static extern void FPDF_ClosePage(IntPtr page);

    [DllImport(pdfiumLib)]
    private static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int start_x, int start_y, 
        int size_x, int size_y, int rotate, int flags);

    [DllImport(pdfiumLib)]
    private static extern IntPtr FPDFBitmap_Create(int width, int height, int alpha);

    [DllImport(pdfiumLib)]
    private static extern void FPDFBitmap_FillRect(IntPtr bitmap, int left, int top, int width, int height, 
        uint color);

    [DllImport(pdfiumLib)]
    private static extern IntPtr FPDFBitmap_GetBuffer(IntPtr bitmap);

    [DllImport(pdfiumLib)]
    private static extern void FPDFBitmap_Destroy(IntPtr bitmap);

    [DllImport(pdfiumLib)]
    private static extern bool FPDF_GetPageSizeByIndex(IntPtr document, int page_index, out double width, out double height);

    public static void InitializePDFium()
    {
        try
        {
            oldPath=String.Empty;
            Debug.Log("Initializing PDFium...");
            FPDF_InitLibrary();
            Debug.Log("PDFium initialized successfully.");
        }
        catch (Exception e)
        {
            Debug.LogError($"PDFium initialization failed: {e.Message}");
            throw;
        }
    }

    public static System.Collections.IEnumerator DownloadAndLoadPDF(string path, string tempPath, Action<string> onSuccess, Action<string> onError)
    {
        Debug.Log($"Attempting to load PDF from {path} to {tempPath}");
        
        using (UnityWebRequest www = UnityWebRequest.Get(path))
        {
            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                Debug.Log($"PDF downloaded successfully, writing to {tempPath}");
                File.WriteAllBytes(tempPath, www.downloadHandler.data);
                if (File.Exists(tempPath))
                {
                    Debug.Log($"PDF file written successfully at {tempPath}");
                    onSuccess?.Invoke(tempPath);
                }
                else
                {
                    Debug.LogError($"Failed to write PDF file to {tempPath}");
                    onError?.Invoke("Error: Failed to write PDF");
                }
            }
            else
            {
                Debug.LogError($"Failed to load PDF from StreamingAssets: {www.error}");
                onError?.Invoke("Error: Failed to load PDF");
            }
        }
    }

    public static bool LoadPDFDocument(string filePath)
    {
        try
        {
            DocumentCleanup();

            Debug.Log($"Loading PDF document from {filePath}");
            document = FPDF_LoadDocument(filePath, null);
            if (document == IntPtr.Zero)
            {
                Debug.LogError("Failed to load PDF document.");
                return false;
            }

            totalPages = FPDF_GetPageCount(document);
            currentPage = 0;
            Debug.Log($"Loaded PDF with {totalPages} pages.");
            
            oldPath=filePath;

            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to load PDF document: {e.Message}");
            return false;
        }
    }

    public static Texture2D LoadPage(int pageIndex, out string errorMessage)
{
    errorMessage = "";
    if (isLoadingPage)
    {
        errorMessage = "Another page is currently loading.";
        Debug.LogWarning(errorMessage);
        return null;
    }

    if (pageIndex < 0 || pageIndex >= totalPages)
    {
        Debug.LogWarning($"Invalid page index: {pageIndex}");
        errorMessage = $"Invalid page index: {pageIndex}";
        return null;
    }

    isLoadingPage = true;
    IntPtr bitmap = IntPtr.Zero;
    Texture2D texture = null;

    try
    {
        if (page != IntPtr.Zero)
        {
            FPDF_ClosePage(page);
            page = IntPtr.Zero;
            Debug.Log($"Closed previous page {currentPage}.");
        }

        page = FPDF_LoadPage(document, pageIndex);
        if (page == IntPtr.Zero)
        {
            Debug.LogError($"Failed to load page {pageIndex}.");
            errorMessage = $"Error: Page {pageIndex + 1} failed";
            return null;
        }

        if (!FPDF_GetPageSizeByIndex(document, pageIndex, out double pageWidth, out double pageHeight))
        {
            Debug.LogWarning("Failed to get page size, using default A4 size.");
            pageWidth = 595;
            pageHeight = 842;
        }

        int renderWidth = 800; // Increased resolution for better clarity
        float scale = renderWidth / (float)pageWidth;
        int renderHeight = Mathf.RoundToInt((float)pageHeight * scale);

#if UNITY_ANDROID
        renderWidth = Mathf.CeilToInt(renderWidth / 4f) * 4;
        renderHeight = Mathf.CeilToInt(renderHeight / 4f) * 4;
#endif

        bitmap = FPDFBitmap_Create(renderWidth, renderHeight, 1);
        if (bitmap == IntPtr.Zero)
        {
            Debug.LogError("Failed to create bitmap.");
            errorMessage = "Error: Rendering failed";
            return null;
        }

        FPDFBitmap_FillRect(bitmap, 0, 0, renderWidth, renderHeight, 0xFFFFFFFF);

        // Enable anti-aliasing and high-quality rendering
        int renderFlags = 0x04 | 0x01; // FPDF_ANNOT | FPDF_LCD_TEXT for better text rendering
        FPDF_RenderPageBitmap(bitmap, page, 0, 0, renderWidth, renderHeight, 0, renderFlags);

        IntPtr buffer = FPDFBitmap_GetBuffer(bitmap);
        int stride = renderWidth * 4;
        byte[] pixelData = new byte[renderHeight * stride];
        Marshal.Copy(buffer, pixelData, 0, pixelData.Length);

        // Convert BGRA to RGBA and flip vertically
        byte[] flippedPixelData = new byte[pixelData.Length];
        for (int y = 0; y < renderHeight; y++)
        {
            for (int x = 0; x < renderWidth; x++)
            {
                int srcIndex = y * stride + x * 4;
                int destIndex = (renderHeight - 1 - y) * stride + x * 4;

                // Convert BGRA to RGBA
                flippedPixelData[destIndex] = pixelData[srcIndex + 2];     // Red
                flippedPixelData[destIndex + 1] = pixelData[srcIndex + 1]; // Green
                flippedPixelData[destIndex + 2] = pixelData[srcIndex];     // Blue
                flippedPixelData[destIndex + 3] = pixelData[srcIndex + 3]; // Alpha
            }
        }

        texture = new Texture2D(renderWidth, renderHeight, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Bilinear; // Improve texture clarity
        texture.LoadRawTextureData(flippedPixelData);
        texture.Apply();

        currentPage = pageIndex;
        Debug.Log($"Loaded page {pageIndex} successfully.");
        return texture;
    }
    catch (Exception e)
    {
        Debug.LogError($"Failed to process page {pageIndex}: {e.Message}");
        errorMessage = $"Error: {e.Message}";
        return null;
    }
    finally
    {
        if (bitmap != IntPtr.Zero)
        {
            FPDFBitmap_Destroy(bitmap);
            Debug.Log("Destroyed bitmap.");
        }
        if (page != IntPtr.Zero)
        {
            FPDF_ClosePage(page);
            page = IntPtr.Zero;
            Debug.Log($"Closed page {pageIndex}.");
        }
        isLoadingPage = false;
    }
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

    private static void DocumentCleanup()
    {
        try
        {
            Debug.Log("Cleaning up document and page...");

            if (page != IntPtr.Zero)
            {
                FPDF_ClosePage(page);
                page = IntPtr.Zero;
                Debug.Log("Closed PDF page.");
            }

            if (document != IntPtr.Zero)
            {
                FPDF_CloseDocument(document);
                document = IntPtr.Zero;
                Debug.Log("Closed PDF document.");
            }
            Debug.Log($"Closed PDF document. {oldPath}");

            // Do NOT destroy the library here!
            RemoveOldPDF();
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to clean up document resources: {e.Message}");
        }
    }

    private static void RemoveOldPDF()
    {
        if (!string.IsNullOrEmpty(oldPath) && File.Exists(oldPath))
        {
            const int maxWaitMs = 5000;
            int waited = 0;
            while (IsFileInUse(oldPath)&& waited < maxWaitMs)
            {
                System.Threading.Thread.Sleep(100); // Wait 100 ms
                waited += 100;
            }

            if (!IsFileInUse(oldPath))
            {
                File.Delete(oldPath);
                Debug.Log("Old file deleted: " + oldPath);
            }
            else
            {
                Debug.LogWarning("File was still locked after 5 seconds. Skipped deletion.");
            }
        }
    }
    private static bool IsFileInUse(string filePath)
    {
        try
        {
            using (FileStream stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                // File is not locked
                return false;
            }
        }
        catch (IOException)
        {
            // File is locked
            return true;
        }
    }
    public static void Cleanup()
    {
        try
        {
            DocumentCleanup();

            FPDF_DestroyLibrary();
            Debug.Log("PDFium library destroyed successfully.");

            oldPath = string.Empty;
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed during full cleanup: {e.Message}");
        }
        
    }
    
}