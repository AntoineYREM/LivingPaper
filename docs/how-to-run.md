# How to run LivingPaper

Two ways: **Docker** (nothing to install but Docker) or **from source** (to work on the code).

> **LivingPaper has no sign-in.** Anyone who can reach it can use the API keys you save in it. Run it on your own computer or
> home network. Do not expose it to the Internet without a reverse proxy that requires a sign-in.

## With Docker

You need Docker with Compose.

```bash
git clone https://github.com/AntoineYREM/LivingPaper.git
cd LivingPaper
docker compose up --build
```

Open <http://localhost:8080>. The first start builds the image (a few minutes), the next ones take seconds.

- **Your data** (books, settings, encrypted keys, prompts) is in the Docker volume `livingpaper-data`, mounted on `/data`. It
  survives `docker compose down` and rebuilds. **`docker compose down -v` deletes it.**
- **The port** is published on `127.0.0.1` only, so only your computer can reach it. To open it to your network, change the line
  in `docker-compose.yml` to `"8080:8080"`, knowing the warning above.
- **The container does not run as root.** The image holds no key, no book and no setting.
- **Update**: `git pull`, then `docker compose up --build -d`. Your volume is kept, and new default prompts are added to your
  `prompts.json`, and a prompt you never edited is replaced by its improved default. A prompt you edited is left as it is.
- **Back up** the volume, keys included: without `/data/keys` the saved API keys cannot be read again.

### Without building: the published image

Each release publishes an image (amd64 and arm64) to GitHub Container Registry, tagged `0.4.0`, `0.4` and `latest`. Save this as
`docker-compose.yml` in an empty folder, next to nothing else:

```yaml
services:
  livingpaper:
    image: ghcr.io/antoineyrem/livingpaper:latest
    restart: unless-stopped
    ports:
      - "127.0.0.1:8080:8080"
    volumes:
      - livingpaper-data:/data
    extra_hosts:
      - "host.docker.internal:host-gateway"

volumes:
  livingpaper-data:
```

Then `docker compose up -d`. To update, `docker compose pull && docker compose up -d`. Pin a version (`:0.4.0`) rather than
`latest` if you want updates only when you decide. The same warning about the port applies.

```bash
# copy the volume to a folder of this computer
docker run --rm -v livingpaper_livingpaper-data:/data -v "$PWD":/backup alpine tar czf /backup/livingpaper-data.tgz -C /data .
```

(The volume is named after the folder that holds `docker-compose.yml`: `docker volume ls` shows the exact name.)

## From source

You need the **.NET 10 SDK** (see `global.json`).

```bash
dotnet run --project src/LivingPaper.Web
```

It listens on <http://localhost:5059> and keeps its data in `./data`, relative to the folder you run the command from (the folder is
ignored by git). Set `DATA_DIRECTORY` to put it elsewhere.

## Configuration

Everything is optional. Set these as environment variables (in Docker, under `environment:` in `docker-compose.yml`).

| Variable | Default | Meaning |
|---|---|---|
| `DATA_DIRECTORY` | `./data` (`/data` in Docker) | Where books, settings, keys and prompts are kept |
| `LANGUAGES` | `en` | Languages of the interface, separated by commas (`en,fr`). English is always offered; the language switch is only shown when there are two or more |
| `EXAMPLES_DIRECTORY` | `examples` next to the application | Where the example books are read from |
| `ANALYSIS_PAGES_PER_PART` | `40` | Pages per part when a book is analyzed; lower it for a model with a small context window |
| `ANALYSIS_PARALLEL_CALLS` | `3` | Parts read at the same time; use `1` for a small local server or strict rate limits |
| `RAG_QUERY_MODE` | `recent` | For a book indexed with a model for vectorization: how the chat searches its passages. `recent`: your message and the three before it, as they are; `rewrite`: the chat model writes the search first (one more call a message: slower, and it costs more); `last`: your message alone |
| `RAG_PASSAGES` | `5` | How many passages the character gets with each message, from 1 to 10. 3 makes the prompt about 1,000 tokens shorter, and the answer is found less often |
| `LANGFUSE_BASE_URL` | none | Address of your Langfuse (`http://localhost:3000`). Required once the two keys are set; there is no default, so nothing is sent to the cloud by surprise |
| `LANGFUSE_PUBLIC_KEY`, `LANGFUSE_SECRET_KEY` | empty | Keys of a Langfuse project. Tracing is **off** while they are empty; setting only one stops the application at start |
| `LANGFUSE_CAPTURE_CONTENT` | `false` | `true` also records the prompts and replies, which hold the text of your books and chats (see [reference](reference.md#traces-of-the-model-calls-langfuse)) |

A value that is not valid stops the application at start with a message that names the variable.

For local development you can copy `.env.example` to `.env` (never committed). With `dotnet run` in the Development environment the
application reads it (in the current folder or a parent one); a variable already set in your terminal wins over the file. Outside
development it is ignored (Docker Compose reads it on its own). The application does not need it: your models are added in the
**Settings** page.

## Add a language model

Open **Settings**, type a name, a base URL and an API key, then press **Get the models**: the application asks the provider for its
list of models, which also checks the key. Choose a model in the list and save. Then choose which model analyzes books and which one
chats.

A third choice, **Model for vectorization**, is optional and set to None. It takes an embedding model (for example `bge-m3`, with
`ollama pull bge-m3`), which turns text into vectors; the model is tried when you choose it. With it, the analysis of a book
keeps its passages and turns them into vectors (about a minute and a half for a long novel with `bge-m3` on a 4 GB graphics
card). The chat then gives the character the passages that answer your message in place of the whole story.

**A local model for the chat needs a context of at least 8,192 tokens** with passages. Some servers start with less (Ollama
uses 4,096 on a 4 GB graphics card): set `OLLAMA_CONTEXT_LENGTH=8192` for Ollama.

A model you add **fills the choices that are empty**: analysis and chat, or vectorization alone when it refuses to chat and can
vectorize. While a choice is empty, the new model is tried once to know which; this can take a minute or two with a slow local
model. A choice already made is never replaced, and deleting a model leaves its choices empty.

If the provider has no list of models (some local servers), the page says so and asks you to type the model name; it is then tested
with a short request (a chat, else a vectorization) before it is saved. The key is kept in memory between the two steps (15 minutes at most, forgotten when the
application restarts) and is never shown again.

| Provider | Base URL | Key |
|---|---|---|
| OpenAI | `https://api.openai.com/v1` | yours |
| A model on this computer (Ollama) | `http://host.docker.internal:11434/v1` from Docker, `http://localhost:11434/v1` from source | any text, or empty |
| A model on this computer (LM Studio) | `http://host.docker.internal:1234/v1` from Docker, `http://localhost:1234/v1` from source | any text, or empty |

From Docker, `localhost` is the container itself: use `host.docker.internal` to reach a server on your computer (`docker-compose.yml`
makes this name work on Linux too). The server must listen on an address the container can reach (for Ollama on Linux, set
`OLLAMA_HOST=0.0.0.0`).

A local model must be able to follow the instructions of the prompts and to answer in JSON for the analysis. A very small model
often cannot: if the analysis fails with "unreadable replies", try a larger one.

## If something goes wrong

- **The page does not open**: `docker compose ps` should show the service as *healthy*; `docker compose logs` shows why not.
- **A book stays "Interrupted"**: open it and press **Retry**; the steps already done are not paid twice.
- **An error names `prompts.json`**: the file you edited is not valid. Fix it, or delete it and restart to get the defaults back.
- **The saved models say their key cannot be read**: the `keys/` folder of the data directory was lost. Add the models again.
