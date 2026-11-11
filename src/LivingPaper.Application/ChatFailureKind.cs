namespace LivingPaper.Application;

// Why a call to a language model failed. The settings page uses it to tell the user what to fix (unreachable URL,
// refused key, unknown model, invalid answer) and the retry policy uses it to know whether trying again can help.
public enum ChatFailureKind
{
    // The provider could not be reached (DNS, connection refused or reset).
    Unreachable,

    // The provider did not answer, or stopped sending, within the time limit.
    TimedOut,

    // The provider answered with an HTTP error status (see ChatProviderException.StatusCode).
    Http,

    // The connection ended before the end marker ("[DONE]"): what arrived is only the start of the reply.
    StreamCut,

    // The provider answered, but not in the format of the chat completions API.
    InvalidResponse,

    // The reply ended without any text.
    EmptyReply,

    // The provider's content filter stopped the reply.
    ContentFiltered,
}
