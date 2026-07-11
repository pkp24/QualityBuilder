# QualityBuilder Unofficial 1.6

Originally created by Hatti: https://steamcommunity.com/sharedfiles/filedetails/?id=754637870
Currently maintained by pkp24 since the original hasn't been updated in a while. Package id is unchanged so this just keeps working as the same mod in your list, no resub needed.

Report bugs on GitHub: https://github.com/pkp24/QualityBuilder

With QualityBuilder you are able to set a minimum quality you want on an object you want to build.
Tired of placing, deconstructing, and replacing 100's of beds just to get some decent ones? This ends now! You can set the minimum quality you want to get from your selected objects.

If the finished object does not meet your minimum quality requirements, it will get deconstructed and replaced in the same position

## What I've fixed/added since taking over

- Updated it to work on RimWorld 1.6
- Fixed a bug where reloading your save would unforbid your quality builder items
- Fixed a bug where deconstructing one of these buildings yourself could sometimes trigger QualityBuilder to rebuild it anyway, even though you wanted it gone
- Added a limit so if a building can't hit your quality target after 3 rebuild attempts, it gives up and keeps the building instead of burning all your resources forever (you'll get a message when this happens)
- Added a slider so you can require builders be within a certain skill level of your best builder, instead of just a flat skill cutoff
- Fixed the "use these map settings" checkbox not actually doing anything
- Fixed some jank where builders could get bumped off a job weirdly, or a quality check could target the wrong building when things overlap on the same tile
- No longer bundles its own copy of Harmony, just uses the Harmony mod like it's supposed to

Claude Code used for the most recent complicated updates, many many hours testing it myself and since the recent changes I've seen 0 quality builder errors while playing.