﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ttaenc
{
    public class MediaFileConverter
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public static readonly string tttool_exe;
        public static readonly string mpg123_exe;
        public static readonly string oggenc_exe;
        public static readonly string oggdec_exe;
        static MediaFileConverter()
        {
            if (OS.IsWindows)
            {
                tttool_exe = "tttool.exe";
                mpg123_exe = "mpg123.exe";
                oggenc_exe = "oggenc2.exe";
                oggdec_exe = "oggdec.exe";
                foreach (var p in new[] {
                "tools",
                @"tools\tttool-win32-1.6",
                @"tools\mpg123-1.22.0-x86-64"
                })
                {
                    var d = Path.Combine(PathUtil.GetDirectory(), p);
                    if (!Directory.Exists(d))
                    {
                        throw new System.IO.FileNotFoundException(d);
                    }
                    PathUtil.AddToPath(d);
                }
            }
            else
            {
                // Unix
                tttool_exe = "tttool";
                mpg123_exe = "mpg123";
                oggenc_exe = "oggenc";
                oggenc_exe = "oggdec";
            }
        }

        const string oggExtension = ".ogg";
        const string mp3Extension = ".mp3";
        const string wavExtension = ".wav";

        bool alwaysConvert = false;

        public async static Task AudioFileToTipToiAudioFile(CancellationToken cancellationToken, string sourceFile, string oggDestinationFile)
        {
            using (new LogScope(log, "Convert {0} to {1}", sourceFile, oggDestinationFile))
            {
                using (var t = new FileTransaction(oggDestinationFile))
                {
                    var ext = Path.GetExtension(sourceFile).ToLowerInvariant();
                    var wavFile = oggDestinationFile + ".wav";
                    try
                    {
                        switch (ext)
                        {
                            case mp3Extension:
                                {
                                    await SubProcess.CheckedCall(cancellationToken
                                        , mpg123_exe,
                                        "-w", wavFile.Quote(),
                                        sourceFile.Quote());

                                    bool isMono = true;

                                    await SubProcess.CheckedCall(cancellationToken,
                                        oggenc_exe,
                                        wavFile.Quote(),
                                        ("--output=" + t.TempPath).Quote(),
                                        "--resample", "22500",
                                        "--quiet",
                                        isMono ? null : "--downmix"
                                        );
                                }
                                break;
                            case oggExtension:
                                {
                                    bool isMono = GetIsMonoFromOggdecOutput(await SubProcess.GetOutput(cancellationToken
                                        , oggdec_exe,
                                        "--wavout", wavFile.Quote(),
                                        "-q",
                                        sourceFile.Quote()));

                                    await SubProcess.CheckedCall(cancellationToken,
                                        oggenc_exe,
                                        wavFile.Quote(),
                                        ("--output=" + t.TempPath).Quote(),
                                        "--resample", "22500",
                                        "--quiet",
                                        isMono ? null : "--downmix"
                                        );
                                }
                                break;
                            case wavExtension:
                                {
                                    await SubProcess.CheckedCall(cancellationToken,
                                        oggenc_exe,
                                        sourceFile.Quote(),
                                        "-o", t.TempPath.Quote(),
                                        "--quiet",
                                        "--resample", "22500",
                                        "--downmix");
                                }
                                break;
                            default:
                                throw new ArgumentOutOfRangeException("sourceFile", sourceFile, "File type is not supported.");
                        }
                    }
                    finally
                    {
                        PathUtil.EnsureFileNotExists(wavFile);
                    }

                    t.Commit();
                }
            }
        }

        public MediaFileConverter(string cacheDirectory)
        {
            this.cacheDirectory = cacheDirectory;
        }

        readonly string cacheDirectory;

        public string OutputDirectory { get { return cacheDirectory; } }

        public async Task<string> ProvidePenAudioFile(CancellationToken cancellationToken, string mp3SourceFile)
        {
            var oggFile = GetPenAudioFilePath(mp3SourceFile);
            if (!File.Exists(oggFile) || alwaysConvert)
            {
                await AudioFileToTipToiAudioFile(cancellationToken, mp3SourceFile, oggFile);
            }
            return oggFile;
        }

        public string GetPenAudioFilePath(string mp3SourceFile)
        {
            var oggFile = Path.Combine(cacheDirectory, Digest.Get(mp3SourceFile.ToLowerInvariant()) + oggExtension);
            return oggFile;
        }

        static bool GetIsMonoFromOggdecOutput(string oggdecOutput)
        {
            return Regex.IsMatch(oggdecOutput, "Bitstream is 1 channel");
        }
    }
}
