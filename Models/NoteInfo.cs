//using System;
//using System.Collections.Generic;
//using System.Text;
//using musicmate.Services;

//namespace musicmate.Models;

//public class NoteInfo2
//{
//    public int Midi { get; set; }
//    public string Name { get; set; } = "";
//    public double TargetFreq { get; set; }
//    public float X { get; set; }

//    // Returns all enharmonic names for this note (including itself)
//    public IEnumerable<string> EnharmonicNames
//    {
//        get
//        {
//            // Use the static method from NoteSessionService
//            var midis = musicmate.Services.NoteSessionService.GetEnharmonicMidis(Midi);
//            foreach (var midi in midis)
//            {
//                // Use both sharp and flat spellings
//                yield return musicmate.Services.NoteSessionService.MidiToNoteName(midi, false);
//                yield return musicmate.Services.NoteSessionService.MidiToNoteName(midi, true);
//            }
//        }
//    }
//}
