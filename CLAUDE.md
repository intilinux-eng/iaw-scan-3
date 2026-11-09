# Rules for this project

## How to work

These four principles come from the ideas in [andrej-karpathy-skills](https://github.com/multica-ai/andrej-karpathy-skills), adapted to this project. For a very small task, use your judgment.

1. **Think before you change code.**
   - Say your assumptions. If you are not sure, ask.
   - If a request has more than one meaning, show the meanings. Do not choose one silently.
   - Do not make one fact general. Example: a rule for the IAW-16F does not apply to all ECUs.
   - For facts about hardware or cars, ask the user, or measure them with the real cable.
2. **Keep it simple.**
   - Write the minimum code that solves the problem.
   - Do not add features, options or abstractions that nobody asked for.
   - If 50 lines can do the work of 200, use 50.
3. **Change only what you must.**
   - Each changed line must come from the request.
   - Use the style of the code around the change.
   - Do not change code, comments or text that work. If you see a problem, tell the user.
   - Remove only what your change made unused.
   - Do not change `IES_3.Core`. The tests compare its decoding with reference values.
4. **Set a goal that you can verify, then verify it.**
   - Before a task with many steps, write a short plan with one check for each step.
   - For a bug, first write a test that shows it, then make the test pass.
   - Check: the build has no warnings, the tests pass on Windows and on Linux (WSL), and the links of the docs work.
   - Tell the user what you could not verify, for example a real ECU.

## Writing

- Write all English text in ASD-STE100 (Simplified Technical English), to about 80%. This includes the READMEs, guides, the English strings of the app, release notes, changelog entries and pull request descriptions.
  - Steps: one instruction in each sentence, in the imperative, with a maximum of 20 words.
  - Descriptions: a maximum of 25 words in each sentence.
  - Use the active voice and simple tenses. Do not use contractions. Use few "-ing" words.
- Write Italian text in plain Italian with short sentences.

## Work

- Use few tokens: read only the parts of files that you need, and keep replies short.
- After a version change, the download links of the README do not work until the release workflow runs.
- Only the user can merge pull requests. After a merge with a version change, run the release workflow at once.
