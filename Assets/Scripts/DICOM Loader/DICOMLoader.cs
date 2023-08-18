using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityVolumeRendering;
using System.Threading.Tasks;

public class DICOMLoader : MonoBehaviour
{
    public string dicomDir;

    async void Start()
    {
        await DicomImportDirectoryAsync(Directory.GetFiles(dicomDir));
    }

    private static async Task DicomImportDirectoryAsync(IEnumerable<string> files)
    {
        IImageSequenceImporter importer = ImporterFactory.CreateImageSequenceImporter(ImageSequenceFormat.DICOM);
        IEnumerable<IImageSequenceSeries> seriesList = await importer.LoadSeriesAsync(files);

        foreach (IImageSequenceSeries series in seriesList)
        {
            VolumeDataset dataset = await importer.ImportSeriesAsync(series);
            VolumeRenderedObject volRendObj = VolumeObjectFactory.CreateObject(dataset);

            // Adjust orientation if needed
            volRendObj.gameObject.transform.Rotate(0, 0, 180);
        }
    }
}

