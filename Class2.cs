﻿using Google.Cloud.Functions.Framework;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using openDicom.Registry;
using openDicom.File;
using openDicom.DataStructure.DataSet;
using openDicom.DataStructure;
using openDicom.Image;

namespace GCP
{
    public class DICOMImporter
    {
        private class DICOMSliceFile
        {
            public AcrNemaFile file;
            public float location = 0;
            public float intercept = 0.0f;
            public float slope = 1.0f;
            public float pixelSpacing = 0.0f;
        }
        public string testname = "HELLO FROM CLASS2";
        private string diroctoryPath;
        private bool recursive;
        private string dicomDictionaryPath = "E:\\internship\\GCP\\CSHARP\\MyCSharpProject\\dicom_output";
        public DICOMImporter(string diroctoryPath, bool recursive)
        {
            this.diroctoryPath = diroctoryPath;
            this.dicomDictionaryPath = diroctoryPath;
            this.recursive = recursive;
        }

        public VolumeDataset Import()
        {
            DataElementDictionary dataElementDictionary = new DataElementDictionary();
            UidDictionary uidDictionary = new UidDictionary();
            try
            {
                Console.WriteLine($"Path: {dicomDictionaryPath}");
                dataElementDictionary.LoadFrom(dicomDictionaryPath, DictionaryFileFormat.BinaryFile);
                uidDictionary.LoadFrom(dicomDictionaryPath, DictionaryFileFormat.BinaryFile);
            }
            catch (Exception dictionaryException)
            {
                // Debug.LogError("Problems processing dictionaries:\n" + dictionaryException);
                Console.Error.WriteLine("Problems processing dictionaries:\n" + dictionaryException);
                return null;
            }

            // Read all files
            IEnumerable<string> fileCandidates = Directory.EnumerateFiles(diroctoryPath, "*.*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .Where(p => p.EndsWith(".dcm") || p.EndsWith(".dicom") || p.EndsWith(".dicm"));
            List<DICOMSliceFile> files = new List<DICOMSliceFile>();
            foreach (string filePath in fileCandidates)
            {
                DICOMSliceFile sliceFile = ReadDICOMFile(filePath);
                if (sliceFile != null)
                    files.Add(sliceFile);
            }
            // Sort files by slice location
            files.Sort((DICOMSliceFile a, DICOMSliceFile b) => { return a.location.CompareTo(b.location); });

            Console.Error.WriteLine($"Imported {files.Count} datasets");

            if (files.Count <= 1)
            {
                Console.Error.WriteLine("Insufficient number of slices.");
                return null;
            }

            float minLoc = (float)files[0].location;
            float maxLoc = (float)files[files.Count - 1].location;
            float locRange = maxLoc - minLoc;

            // Create dataset
            VolumeDataset dataset = new VolumeDataset();
            dataset.datasetName = Path.GetFileName(Path.GetDirectoryName(diroctoryPath));
            dataset.dimX = files[0].file.PixelData.Columns;
            dataset.dimY = files[0].file.PixelData.Rows;
            dataset.dimZ = files.Count;

            int dimension = dataset.dimX * dataset.dimY * dataset.dimZ;
            dataset.data = new int[dimension];

            for (int iSlice = 0; iSlice < files.Count; iSlice++)
            {
                DICOMSliceFile slice = files[iSlice];
                PixelData pixelData = slice.file.PixelData;
                int[] pixelArr = ToPixelArray(pixelData);
                if (pixelArr == null) // This should not happen
                    pixelArr = new int[pixelData.Rows * pixelData.Columns];

                for (int iRow = 0; iRow < pixelData.Rows; iRow++)
                {
                    for (int iCol = 0; iCol < pixelData.Columns; iCol++)
                    {
                        int pixelIndex = (iRow * pixelData.Columns) + iCol;
                        int dataIndex = (iSlice * pixelData.Columns * pixelData.Rows) + (iRow * pixelData.Columns) + iCol;

                        int pixelValue = pixelArr[pixelIndex];
                        float hounsfieldValue = pixelValue * slice.slope + slice.intercept;

                        // dataset.data[dataIndex] = (int)Mathf.Clamp(hounsfieldValue, -1024.0f, 3071.0f);
                        dataset.data[dataIndex] = (int)Math.Min(Math.Max(hounsfieldValue, -1024.0f), 3071.0f);
                    }
                }
            }

            if (files[0].pixelSpacing > 0.0f)
            {
                dataset.scaleX = files[0].pixelSpacing * dataset.dimX;
                dataset.scaleY = files[0].pixelSpacing * dataset.dimY;
                // dataset.scaleZ = Mathf.Abs(files[files.Count - 1].location - files[0].location);
                dataset.scaleZ = Math.Abs(files[files.Count - 1].location - files[0].location);
            }

            return dataset;
        }

        private DICOMSliceFile ReadDICOMFile(string filePath)
        {
            AcrNemaFile file = LoadFile(filePath);

            if (file != null && file.HasPixelData)
            {
                DICOMSliceFile slice = new DICOMSliceFile();
                slice.file = file;
                // Read location
                Tag locTag = new Tag("(0020,1041)");
                if (file.DataSet.Contains(locTag))
                {
                    DataElement elemLoc = file.DataSet[locTag];
                    slice.location = (float)Convert.ToDouble(elemLoc.Value[0]);
                }
                else
                {
                    Console.Error.WriteLine($"Missing location tag in file: {filePath}.\n The file will not be imported");
                    return null;
                }
                // Read intercept
                Tag interceptTag = new Tag("(0028,1052)");
                if (file.DataSet.Contains(interceptTag))
                {
                    DataElement elemIntercept = file.DataSet[interceptTag];
                    slice.intercept = (float)Convert.ToDouble(elemIntercept.Value[0]);
                }
                else
                    Console.Error.WriteLine($"The file {filePath} is missing the intercept element. As a result, the default transfer function might not look good.");
                // Read slope
                Tag slopeTag = new Tag("(0028,1053)");
                if (file.DataSet.Contains(slopeTag))
                {
                    DataElement elemSlope = file.DataSet[slopeTag];
                    slice.slope = (float)Convert.ToDouble(elemSlope.Value[0]);
                }
                else
                    Console.Error.WriteLine($"The file {filePath} is missing the intercept element. As a result, the default transfer function might not look good.");
                // Read pixel spacing
                Tag pixelSpacingTag = new Tag("(0028,0030)");
                if (file.DataSet.Contains(pixelSpacingTag))
                {
                    DataElement elemPixelSpacing = file.DataSet[pixelSpacingTag];
                    slice.pixelSpacing = (float)Convert.ToDouble(elemPixelSpacing.Value[0]);
                }

                return slice;
            }
            return null;
        }

        private AcrNemaFile LoadFile(string filePath)
        {
            AcrNemaFile file = null;
            try
            {
                if (DicomFile.IsDicomFile(filePath))
                    file = new DicomFile(filePath, false);
                else if (AcrNemaFile.IsAcrNemaFile(filePath))
                    file = new AcrNemaFile(filePath, false);
                else
                    Console.Error.WriteLine("Selected file is neither a DICOM nor an ACR-NEMA file.");
            }
            catch (Exception dicomFileException)
            {
                Console.Error.WriteLine($"Problems processing the DICOM file {filePath} :\n {dicomFileException}");
                return null;
            }
            return file;
        }

        private static int[] ToPixelArray(PixelData pixelData)
        {
            int[] intArray;
            if (pixelData.Data.Value.IsSequence)
            {
                Sequence sq = (Sequence)pixelData.Data.Value[0];
                intArray = new int[sq.Count];
                for (int i = 0; i < sq.Count; i++)
                    intArray[i] = Convert.ToInt32(sq[i].Value[0]);
                return intArray;
            }
            else if (pixelData.Data.Value.IsArray)
            {
                Array arr = (Array)pixelData.Data.Value[0];
                intArray = new int[arr.Length];
                for (int i = 0; i < arr.Length; i++)
                    intArray[i] = Convert.ToInt32(arr.GetValue(i));
                return intArray;
            }
            else
            {
                Console.Error.WriteLine("Pixel array is invalid");
                return null;
            }
        }
    }
}
