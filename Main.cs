/*
Author: Richard Leszczynski
Email: richard@makerdyne.com
Website: http://www.makerdyne.com
*/

using System;
using System.IO;
using System.Diagnostics;
using System.Text;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;


namespace RecursiveChecksummer
{
	class MainClass
	{
		// Source and destination root directories
		static string rootSource = null;
		static string rootDest = null;
		static string fileWithChecksums;
		static bool createFileWithChecksums = false;
		static int numCores = Environment.ProcessorCount;
		static StreamWriter fwcWriter;
		static StreamReader fwcReader;
		static ushort programMode = 0;
		static bool parallelSupport = false;
		static bool useDotNetMD5 = false;
		// Source
		static uint numSourceFiles = 0;
		static uint numSourceDirs = 1;
		static ConcurrentBag<string> sourceFilesToProcess = new ConcurrentBag<string>();
		static ConcurrentDictionary<string, string> sourceFilesWithChecksums = new ConcurrentDictionary<string, string>(numCores,1000);
		static ConcurrentBag<string> sourceFilesWithoutChecksums = new ConcurrentBag<string>();	// When there's a problem generating a checksum
		static SortedList<string, string> sortedSourceFilesWithChecksums = new SortedList<string, string>();
		static SortedSet<string> sortedSourceFilesWithoutChecksums = new SortedSet<string>();
		// Destination
		static uint numDestFiles = 0;
		static uint numDestDirs = 1;
		static ConcurrentBag<string> destFilesToProcess = new ConcurrentBag<string>();
		static ConcurrentDictionary<string, string> destFilesWithChecksums = new ConcurrentDictionary<string, string>(numCores,1000);
		static ConcurrentBag<string> destFilesWithoutChecksums = new ConcurrentBag<string>(); 	// When there's a problem generating a checksum
		static SortedList<string, string> sortedDestFilesWithChecksums = new SortedList<string, string>();
		static SortedSet<string> sortedDestFilesWithoutChecksums = new SortedSet<string>();
		// Difference
		static ConcurrentBag<string> filesNoMatch = new ConcurrentBag<string>();
		static ConcurrentBag<string> filesInSourceNotDest = new ConcurrentBag<string>();
		static ConcurrentBag<string> filesInDestNotSource = new ConcurrentBag<string>();
		static SortedSet<string> sortedFilesNoMatch = new SortedSet<string>();
		static SortedSet<string> sortedFilesInSourceNotDest = new SortedSet<string>();
		static SortedSet<string> sortedFilesInDestNotSource = new SortedSet<string>();
		
		public static int Main(string[] args)
		{
			
			/* PROCESS COMMAND LINE ARGUMENTS
			 * Command line flags:
			 * -s = root source directory
			 * -d = root destination directory
			 * -f = path to a file containing checksums of a set of files to check
			 * -h = print program usage details and exit
			 */
			
			for(uint i = 0; i < args.GetLength(0); i++)
			{
				switch(args[i])
				{
				case "-s":
					// signifies that the next CLA will be the root source directory
					if(rootSource != null) {
						Console.WriteLine("ERROR: multiple source (-s) directories have been specified. Please only specify a single source directory");
						printUsageInformation();
						return 1;
					}
					if(fileWithChecksums != null) {
						Console.WriteLine("ERROR: Please specify source (-s) and/or destination (-d) directories before specifying a file to hold checksums");
						printUsageInformation();
						return 1;
					}
					rootSource = args[++i];
					rootSource = rootSource.TrimEnd('/') + '/';
					if(!rootSource.StartsWith("/"))	// Deal with relative directory paths in the command line args
						rootSource = Environment.CurrentDirectory + "/" + rootSource;
					if(!Directory.Exists(rootSource)) {
						Console.WriteLine("ERROR: Source (-s) directory does not exist!");
						return 1;
					}
					break;
				case "-d":
					// signifies that the next CLA will be the root destination directory
					if(rootDest != null) {
						Console.WriteLine("ERROR: multiple destination (-d) directories have been specified. Please only specify a single destination directory");
						printUsageInformation();
						return 1;
					}
					if(fileWithChecksums != null) {
						Console.WriteLine("ERROR: Please specify source (-s) and/or destination (-d) directories before specifying a file to hold checksums");
						printUsageInformation();
						return 1;
					}
					rootDest = args[++i];
					rootDest = rootDest.TrimEnd('/') + '/';
					if(!rootDest.StartsWith("/"))	// Deal with relative directory paths in the command line args
						rootDest = Environment.CurrentDirectory + "/" + rootDest;
					if(!Directory.Exists(rootDest)) {
						Console.WriteLine("ERROR: Destination (-d) directory does not exist!");
						return 1;
					}
					break;
				case "-f":
					/* signifies that the next CLA will be a file containing checksums of files
					 * MODES OF OPERATION:
					 * 1. if -s is already specified, -f signifies a file which is to be created or overwritten to hold the checksums of all files in rootSource
					 * 2. if -s and -d are both already specified, -f MAY be used to signify a file which is to be created or overwritten to store the checksums of all
					 *    files in rootSource.
					 * 3. if -d is already specified, -f signifies an existing file which contains the checksums that all files in destsource are to be checked against
					 */
					if(fileWithChecksums != null) {
						Console.WriteLine("ERROR: multiple files to hold checksums (-f) have been specified. Please only specify a single file");
						printUsageInformation();
						return 1;
					}
					fileWithChecksums = args[++i];
					if(!fileWithChecksums.StartsWith("/"))	// Deal with relative directory paths in the command line args
						fileWithChecksums = Environment.CurrentDirectory + "/" + fileWithChecksums;
					if(!File.Exists(fileWithChecksums)) {
//						Console.WriteLine("ERROR: File to hold checksums (-f) does not exist!");
						try {
							File.Create(fileWithChecksums).Close();
						}
						catch(Exception ex) {
							Console.WriteLine("ERROR: Problem creating file to hold checksums");
							Console.WriteLine(ex.Message);
						}
					}
					break;
				case "-c":
					// determines the number of concurrent instances of md5sum to run. Cannot be greater than the number of processor cores
					ushort numCoresAvailable = (ushort)Environment.ProcessorCount;
					if(numCoresAvailable<1)
						numCoresAvailable = 1;
					try {
						numCores = Convert.ToUInt16(args[++i]);
					}
					catch(Exception ex) {
						Console.WriteLine("ERROR: Problem with the value specified for the number of cores (-c) argument: {0}", args[i]);
						Console.WriteLine(ex.Message);
						return 1;
					}
					if(numCores == 0)
						numCores=1;
					else if(numCores > Environment.ProcessorCount) {
						numCores = (ushort)Environment.ProcessorCount;
					}
					break;
				case "-h":
					// print program usage information and exit
					printUsageInformation();
					return 0;
				default:
					// print program usage information and exit
					printUsageInformation();
					return 1;
				}
			}
						
			// PRE-RUN CHECKS
			// Check that md5sum exists
			if(!File.Exists(@"/bin/md5sum")) {
				Console.WriteLine("WARNING: The Linux program which calculates the checksums (md5sum) either does not exist or cannot be found");
				Console.WriteLine("Falling back to using .Net's own checksum generating functions");
				useDotNetMD5 = true;
			}
			// TODO: Either delete this check of add if(parallelSupport){Parallel.For...createChecksum(..))else{Standard.For} below
			// Check for parallel.for support (has been problematic)
			try {
				Parallel.For((int)0, (int)numCores, i => {
					parallelSupport = true;
				}); // end Parallel.For
			}
			catch(Exception ex) {
				Console.WriteLine("WARNING: Parallelisation support not available in this version of Mono, Value of parallelSupport is {0}", parallelSupport);
				Console.WriteLine(ex.Message);
			}
			
			// DETERMINE IF PROGRAM IS TO RUN IN MODE 1,2 or 3
			// Determine Mode 1
			if((rootSource != null) && (rootDest == null)) {
				if(fileWithChecksums != null) {
					programMode = 1;
					createFileWithChecksums = true;
				}
				else {
					Console.WriteLine("ERROR: A file to hold checksums (-f) must be specified if only -s and not -d is given as an argument");
					printUsageInformation();
					return 1;
				}
			}
			// Determine Mode 2
			else if((rootSource != null) && (rootDest != null)) {
				programMode = 2;
				// check for identical source and destination directories
				if(rootSource.TrimEnd('/') == rootDest.TrimEnd('/')) {
					Console.WriteLine("ERROR: Source (-s) and destination (-d) directories are the same. Please ensure that they are different");
					printUsageInformation();
					return 1;
				}
				createFileWithChecksums = (fileWithChecksums == null)?false:true;
			}
			// Determine Mode 3
			else if((rootSource == null) && (rootDest != null)) {
				if(fileWithChecksums != null)
					programMode = 3;
				else {
					Console.WriteLine("ERROR: A file to hold checksums (-f) must be specified if only -d and not -s is given as an argument");
					printUsageInformation();
					return 1;
				}
			}
			// Determine insufficient arguments for any mode
			else if((rootSource == null) && (rootDest == null)) {
				Console.WriteLine("Error: Neither a source (-s) nor destination (-d) directory has been specified");
				printUsageInformation();
				return 1;
			}
			
			// Create streams required for modes.
			// Do not use any encoding options when creating the StreamWriter. The Linux utility md5sum in -c mode needs a file *without* any byte-order mark at the beginning
			try {
				switch(programMode) {
				case 1:
				case 2:
					if(createFileWithChecksums)
						fwcWriter = new StreamWriter(fileWithChecksums, false);
					break;
				case 3:
					fwcReader = new StreamReader(fileWithChecksums);
					break;
				default:
					Console.WriteLine("ERROR: An unidentified program mode has been selected. programMode = {0}", programMode);
					printUsageInformation();
					return 1;
				}
			}
			catch(Exception ex) {
				Console.WriteLine("ERROR: Cannot access file {0] to hold checksums in {1}",
				                  Path.GetFileName(fileWithChecksums), Path.GetDirectoryName(fileWithChecksums));
				Console.WriteLine(ex.Message);
				return 1;
			}
			// Set parallel processing options
			ParallelOptions pOpts = new ParallelOptions();
			pOpts.MaxDegreeOfParallelism = numCores;
					
			// PROGRAM EXECUTION
			// Generate list of source files to operate on
			if(programMode == 1 || programMode == 2) {
				try {
					generateFileLists(sourceFilesToProcess, rootSource, rootSource, ref numSourceFiles, ref numSourceDirs);
				}
				catch(Exception ex) {
					Console.WriteLine("ERROR: Exception thrown from within generateFileLists");
					Console.WriteLine(ex.Message);
					Console.WriteLine(ex.StackTrace);
				}
				Parallel.ForEach(sourceFilesToProcess, pOpts, currentFile => {
					createChecksum(rootSource, currentFile, sourceFilesWithChecksums, sourceFilesWithoutChecksums);
				}); // end Parallel.For
				
				// Sort and store the list of files with checksums to a file
				foreach(string file in sourceFilesWithChecksums.Keys) {
					sortedSourceFilesWithChecksums.Add(file, sourceFilesWithChecksums[file]);
				}
				foreach(string file in sourceFilesWithoutChecksums) {
					sortedSourceFilesWithoutChecksums.Add(file);
				}
				if(sortedSourceFilesWithoutChecksums.Count != 0) {
					Console.WriteLine("ERROR: There were files within the source directory tree for which checksums could not be generated. It is likely the program did not have sufficient privileges to read these files");
					foreach(string file in sortedSourceFilesWithoutChecksums) {
						Console.WriteLine("Could not create checksum for file in source: {0}", file);
					}
					return 1;
				}
			}

			if(programMode == 3) {
				// read contents of fileWithChecksums into sourceFilesWithChecksums
				string line = null;
				string checksum = null;
				string file = null;
				int doubleSpacePos = 0;
				while((line = fwcReader.ReadLine()) != null) {
					// extract the checksum from the line (successful md5sum output should be 1 line: checksum followed by two spaces and the relative file-path)
					checksum = null;
					file = null;
					doubleSpacePos = line.IndexOf("  ");
					if(doubleSpacePos == -1) {
						Console.WriteLine("ERROR: Problem reading checksums and filenames from {0}. Could not find double whitespace separator between checksum and filename", fileWithChecksums);
						// TODO: decide whether to return 1 or add the offending file to sourceFilesWithoutChecksums
						// NB: there may not be a filename if there's no double space found...
						// Try adding the whole line to filesWithoutChecksums, so the user can see the unedited problematic lines
						return 1;
					}
					try {
						checksum = line.Remove(doubleSpacePos);
						file = line.Substring(line.IndexOf("  ")+2);
					}
					catch(ArgumentOutOfRangeException ex) {
						Console.WriteLine("ERROR: Problem reading checksums and filenames from {0}. Could not separate out checksum and filename", fileWithChecksums);
						fwcReader.Close();
						return 1;
					}
					if(!sourceFilesWithChecksums.TryAdd(file, checksum)) { // NB: note the use of *source*FilesWithChecksums to hold the file contents
						Console.WriteLine("ERROR: Problem parsing the file with checksums {0}, it contains duplicate lines", fileWithChecksums);
						return 1;
					}
				}
				fwcReader.Close();
				foreach(string fileWithChecksum in sourceFilesWithChecksums.Keys)
					sortedSourceFilesWithChecksums.Add(fileWithChecksum, sourceFilesWithChecksums[fileWithChecksum]);
			}
			// Generate list of destination files to operate on
			if(programMode == 2 || programMode == 3) {
				generateFileLists(destFilesToProcess, rootDest, rootDest, ref numDestFiles, ref numDestDirs);
				Parallel.ForEach(destFilesToProcess, pOpts, currentFile => {
					createChecksum(rootDest, currentFile, destFilesWithChecksums, destFilesWithoutChecksums);
				}); // end Parallel.For
				
				// Sort and store the list of files with checksums to a file
				foreach(string file in destFilesWithChecksums.Keys) {
					sortedDestFilesWithChecksums.Add(file, destFilesWithChecksums[file]);
				}
				foreach(string file in destFilesWithoutChecksums) {
					sortedDestFilesWithoutChecksums.Add(file);
				}
				if(sortedDestFilesWithoutChecksums.Count != 0) {
					Console.WriteLine("ERROR: There were files within the destination directory tree for which checksums could not be generated. It is likely the program did not have sufficient privileges to read these files");
					foreach(string file in sortedDestFilesWithoutChecksums) {
						Console.WriteLine("Could not create checksum for file in destination: {0}", file);
					}
					return 1;
				}
			}
		
			// the writing of -f has been deliberately moved to after all processing has finished on the source and destination directories 
			if(programMode == 1 || programMode == 2) {
				if(createFileWithChecksums) {
					foreach(string file in sortedSourceFilesWithChecksums.Keys)
						fwcWriter.WriteLine(string.Format("{0}  {1}", sortedSourceFilesWithChecksums[file], file));
					fwcWriter.Close();
				}
			}
			
			// Compare the source and destination file lists
			// Find files in both source and destination but which have different checksums
			foreach(string file in sortedSourceFilesWithChecksums.Keys) {
				if(sortedDestFilesWithChecksums.ContainsKey(file)) {
					if(sortedSourceFilesWithChecksums[file] != sortedDestFilesWithChecksums[file])
						filesNoMatch.Add(file);
				}
				else // file is in source but not destination
					filesInSourceNotDest.Add(file);
			}
			// Find files in destination but not source
			foreach(string file in sortedDestFilesWithChecksums.Keys) {
				if(!sortedSourceFilesWithChecksums.ContainsKey(file))
					filesInDestNotSource.Add(file);
			}
			
			// DEVELOPMENT INFORMATION SUMMARY
			Console.WriteLine();
			Console.WriteLine("File with checksums is {0}", fileWithChecksums);
			Console.WriteLine("Program mode is {0}", programMode);
			Console.WriteLine("Number of cores requested is {0}", numCores);
			Console.WriteLine();
			Console.WriteLine("Number of files in sourceFilesToProcess is {0}", sourceFilesToProcess.Count);
			Console.WriteLine("Number of files in sourceFilesWithChecksums is {0}", sourceFilesWithChecksums.Count);
			Console.WriteLine("Number of files in sourceFilesWithoutChecksums is {0}", sourceFilesWithoutChecksums.Count);
			Console.WriteLine("Number of files in sortedSourceFilesWithChecksums is {0}", sortedSourceFilesWithChecksums.Count);
			Console.WriteLine("Number of files in sortedSourceFilesWithoutChecksums is {0}", sortedSourceFilesWithoutChecksums.Count);
			Console.WriteLine();
			Console.WriteLine("Number of files in destFilesToProcess is {0}", destFilesToProcess.Count);
			Console.WriteLine("Number of files in destFilesWithChecksums is {0}", destFilesWithChecksums.Count);
			Console.WriteLine("Number of files in destFilesWithoutChecksums is {0}", destFilesWithoutChecksums.Count);
			Console.WriteLine("Number of files in sortedDestFilesWithChecksums is {0}", sortedDestFilesWithChecksums.Count);
			Console.WriteLine("Number of files in sortedDestFilesWithoutChecksums is {0}", sortedDestFilesWithoutChecksums.Count);
			Console.WriteLine();
			Console.WriteLine("Number of files in filesNoMatch is {0}", filesNoMatch.Count);
			Console.WriteLine("Number of files in filesInSourceNotDest is {0}", filesInSourceNotDest.Count);
			Console.WriteLine("Number of files in filesInDestNotSource is {0}", filesInDestNotSource.Count);
			Console.WriteLine("Number of files in sortedFilesNoMatch is {0}", sortedFilesNoMatch.Count);
			Console.WriteLine("Number of files in sortedFilesInSourceNotDest is {0}", sortedFilesInSourceNotDest.Count);
			Console.WriteLine("Number of files in sortedFilesInDestNotSource is {0}", sortedFilesInDestNotSource.Count);
			Console.WriteLine();
			
			// TODO: Compare file sizes of source and destination files before computing checksums for them
			// TODO: Store file sizes as well as checksums in the -f file/ - like cksum outputs: sum size filepath
			// TODO: Compare speed of md5sum with .NET's own cryptographic functions
			// TODO: Implement stopwatch
			// TODO: Implement verbose mode that outputs progress information to console if a -v command line arg is given
			// 		 Stage of process: generating source/dest file lists, generating source/dest checksums, reading/writing file with checksums, sorting source/dest/match file lists
			
			// RESULTS OF COMPARISON
			Console.WriteLine();
			Console.WriteLine("------------------------------------------------------------------------------");
			Console.WriteLine("RecursiveChecksummer: Results of source and destination directory comparison:");
			Console.WriteLine("------------------------------------------------------------------------------\n");
			if(programMode == 1 || programMode == 2) {
				if(fileWithChecksums != null)
					Console.WriteLine("An md5sum -c compatible file holding the checksums of all the files in the source directory has been created at\n{0}\n", fileWithChecksums);
				Console.WriteLine("Source directory: {0}", rootSource);
				Console.WriteLine("Source contained {0} files in {1} directories\n", numSourceFiles, numSourceDirs);
			}
			if(programMode == 2 || programMode == 3) {
				Console.WriteLine("Destination directory: {0}", rootDest);
				Console.WriteLine("Destination contained {0} files in {1} directories\n", numDestFiles, numDestDirs);
				if((filesNoMatch.Count == 0) && (filesInSourceNotDest.Count == 0) && (filesInDestNotSource.Count == 0)) {
					Console.WriteLine("SUCCESS: No differences found between source and destination directories\n");
				}
				else {
					Console.WriteLine("WARNING: The following differences were found between the source and destination directories\n");
					if(filesNoMatch.Count != 0) {
						Console.WriteLine("The following {0} files are different between the source and destination directories", filesNoMatch.Count);
						foreach(string file in filesNoMatch)
							Console.WriteLine(file);
						Console.WriteLine();
					}
					if(filesInSourceNotDest.Count != 0) {
						Console.WriteLine("The following {0} files were present in the source directories but not in the destination directories:", filesInSourceNotDest.Count);
						foreach(string file in (filesInSourceNotDest))
							Console.WriteLine(file);
						Console.WriteLine();
					}
					if(filesInDestNotSource.Count != 0) {
						Console.WriteLine("The following {0} files were present in the destination directories but not in the source directories:", filesInDestNotSource.Count);
						foreach(string file in (filesInDestNotSource))
							Console.WriteLine(file);
						Console.WriteLine();
					}
				}
			}
			return 0;
		}
		
		// Function to generate lists of files to be passed to the checksumming program (md5sum) (function used for Mode 1 and 2)
		static void generateFileLists(ConcurrentBag<string> fileList, string rootDir, string currentDir, ref uint fileCounter, ref uint dirCounter)
		{
			// TODO: resolve problem of /proc /sys /dev and other unwelcome directories
			string[] files = Directory.GetFiles(currentDir);
			string[] dirs = Directory.GetDirectories(currentDir);
			foreach(string file in files) {
				fileList.Add(file.Replace(rootDir,""));
				++fileCounter;
			}
			foreach(string dir in dirs) {
				++dirCounter;
				generateFileLists(fileList, rootDir, dir, ref fileCounter, ref dirCounter);
			}
		}

		static void createChecksum(string workingDir, string file, ConcurrentDictionary<string, string> successList, ConcurrentBag<string> failureList)
		{
			if(useDotNetMD5) {	// use .Net's own cryptographic functions to generate a checksum
				byte[] checksumArray;
				string checksum;
				try {
					FileStream fileToOpen = new FileStream(workingDir+file, FileMode.Open);	// TODO: add option for sequential read
					MD5 md5 = new MD5CryptoServiceProvider();
					checksumArray = md5.ComputeHash(fileToOpen);
					fileToOpen.Close();
					checksum = (BitConverter.ToString(checksumArray)).Replace("-","").ToLower();
					successList.AddOrUpdate(file, checksum, (sKey, sVal) => checksum);
				}
				catch(Exception ex) {
					failureList.Add(file);
					Console.WriteLine(ex.Message);
				}	
			}
			else {	// use the Linux utility md5sum
				string commandArgs = String.Format("\"{0}\"", file);
				string command = "md5sum";
				
				ProcessStartInfo procSettings = new ProcessStartInfo();
				procSettings.FileName = command;
				procSettings.Arguments = commandArgs;
				procSettings.CreateNoWindow = true;
				procSettings.UseShellExecute = false;
				procSettings.WorkingDirectory = workingDir;
				procSettings.RedirectStandardOutput = true;
				try {
					using(Process checksummer = Process.Start(procSettings)) {
						using(StreamReader checksummerOutput = checksummer.StandardOutput) {
							// process the output
							checksummer.WaitForExit();
							string outputLine;
							if(checksummer.ExitCode == 0 && (outputLine = checksummerOutput.ReadLine()) != null) {
								// extract the checksum from the line (successful md5sum output should be 1 line: checksum followed by two spaces and the relative file-path)
								string checksum = outputLine.Remove(outputLine.IndexOf(' '));
								successList.AddOrUpdate(file, checksum, (sKey, sVal) => checksum); // TODO: decide whether to use AddOrUpdate or Add method, and why?
							}
							else {
								failureList.Add(file);
							}
						}
					}
				}
				catch(Exception ex) {
					Console.WriteLine("ERROR: Problem running md5sum");
					Console.WriteLine(ex.Message);
				}
			}
		}

		static void printUsageInformation()
		{
			Console.WriteLine("Recursive Checksummer");
			Console.WriteLine("Program to generate or check checksums of all files within a directory tree");
			Console.WriteLine("Usage: ");
		}
	}
}
