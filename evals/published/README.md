# Published runs

Runs of the evaluation tool that were chosen to be shared in the repository: the figures behind the results of the README.

Every run the tool makes is saved in `evals/runs/`, which git ignores because a run holds the text of the questions and of the replies.
To publish one, copy its file here:

```bash
cp evals/runs/20261123-140512-gpt-5-mini.json evals/published/
```

Before you commit it:

- Read it. It holds the replies of a language model: check that nothing in them should not be public.
- It holds no key and no address; the models are named by the name given on the Settings page. A name you chose is published as it is.
- Keep the file as it is: its name is the id of the run, and the scores in Langfuse (if the run was sent there) carry that id.
