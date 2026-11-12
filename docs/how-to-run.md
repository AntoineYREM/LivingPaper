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
  `prompts.json` without touching yours.
- **Back up** the volume, keys included: without `/data/keys` the saved API keys cannot be read again.

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
| `EXAMPLES_DIRECTORY` | `examples` next to the application | Where the example books are read from |
| `ANALYSIS_PAGES_PER_PART` | `40` | Pages per part when a book is analyzed; lower it for a model with a small context window |
| `ANALYSIS_PARALLEL_CALLS` | `3` | Parts read at the same time; use `1` for a small local server or strict rate limits |

A value that is not valid stops the application at start with a message that names the variable.

For local development you can copy `.env.example` to `.env` (never committed). The application itself does not need it: your models
are added in the **Settings** page.

## Add a language model

Open **Settings**, add a model (a name, a base URL, an API key, a model name). A connection test runs before it is saved. Then
choose which model analyzes books and which one chats.

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
