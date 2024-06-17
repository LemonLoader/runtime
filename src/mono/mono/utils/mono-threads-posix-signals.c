/**
 * \file
 * Shared facility for Posix signals support
 *
 * Author:
 *	Ludovic Henry (ludovic@gmail.com)
 *
 * (C) 2015 Xamarin, Inc
 */

#include <config.h>
#include <glib.h>

#include "mono-threads.h"

#include <mono/metadata/mono-private-unstable.h>

#if defined(USE_POSIX_BACKEND)

#include <errno.h>
#include <mono/utils/mono-errno.h>
#include <signal.h>

#ifdef HAVE_ANDROID_LEGACY_SIGNAL_INLINES_H
#include <android/legacy_signal_inlines.h>
#include <metadata/threads.h>
#endif

#include "mono-threads-debug.h"
#include "mono-threads-coop.h"

gint
mono_threads_suspend_search_alternative_signal (void)
{
#if !defined (SIGRTMIN)
	g_error ("signal search only works with RTMIN");
#else
	int i;
	/* we try to avoid SIGRTMIN and any one that might have been set already, see bug #75387 */
	for (i = SIGRTMIN + 1; i < SIGRTMAX; ++i) {
		struct sigaction sinfo;
		sigaction (i, NULL, &sinfo);
		if (sinfo.sa_handler == SIG_DFL && (void*)sinfo.sa_sigaction == (void*)SIG_DFL) {
			return i;
		}
	}
	g_error ("Could not find an available signal");
#endif
}

static int suspend_signal_num = -1;
static int restart_signal_num = -1;
static int abort_signal_num = -1;

static sigset_t suspend_signal_mask;
static sigset_t suspend_ack_signal_mask;

struct MergedSignalHandle {
	struct sigaction* new;
	struct sigaction* original;
};
typedef struct MergedSignalHandle t_merged_signal;

GHashTable* mono_ml_signal_handler_patched = NULL;
CheckThread mini_current_thread_checker = NULL;

void
mono_melonloader_set_thread_checker (CheckThread checker)
{
	mini_current_thread_checker = checker;
}

static mono_bool mono_il2cpp_check_current_thread(MonoNativeThreadId tid) {
	THREADS_SUSPEND_DEBUG("Checking thread %p", mono_native_thread_id_get());

	if (!mini_current_thread_checker) {
		THREADS_SUSPEND_DEBUG("No checker set, returning true");
		return TRUE;
	}

	return mini_current_thread_checker(tid);
}

static void
restart_signal_handler (int _dummy, siginfo_t *_info, void *context)
{
	MonoThreadInfo *info;

	info = mono_thread_info_current ();

	THREADS_SUSPEND_DEBUG("Restarting %p", mono_thread_info_get_tid (info));

	info->signal = restart_signal_num;
}

static void
suspend_signal_handler (int _dummy, siginfo_t *info, void *context)
{
    MonoThreadInfo *current = mono_thread_info_current ();

    THREADS_SUSPEND_DEBUG ("SIGNAL HANDLER FOR %p [%p]\n", mono_thread_info_get_tid (current), (void*)current->native_handle);
    if (current->syscall_break_signal) {
        current->syscall_break_signal = FALSE;
        THREADS_SUSPEND_DEBUG ("syscall break for %p\n", mono_thread_info_get_tid (current));
        mono_threads_notify_initiator_of_abort (current);
        goto done;
    }

    THREADS_SUSPEND_DEBUG("[%p] state machine", mono_thread_info_get_tid (current));

    /* Have we raced with self suspend? */
    if (!mono_threads_transition_finish_async_suspend (current)) {
        current->suspend_can_continue = TRUE;
        THREADS_SUSPEND_DEBUG ("lost race with self suspend %p\n", mono_thread_info_get_tid (current));
        /* Under full preemptive suspend, there is no self suspension,
         * so no race.
         *
         * Under full cooperative suspend, there is no signal, so no
         * race.
         *
         * Under hybrid a blocking thread could race done/abort
         * blocking with the signal handler running: if the done/abort
         * blocking win, they will wait for a resume - the signal
         * handler should notify the suspend initiator that the thread
         * suspended, and then immediately return and let the thread
         * continue waiting on the resume semaphore.
         */
        g_assert (mono_threads_is_hybrid_suspension_enabled ());
        mono_threads_notify_initiator_of_suspend (current);
        goto done;
    }

    THREADS_SUSPEND_DEBUG("[%p] suspend complete", mono_thread_info_get_tid (current));


    /*
     * If the thread is starting, then thread_state_init_from_sigctx returns FALSE,
     * as the thread might have been attached without the domain or lmf having been
     * initialized yet.
     *
     * One way to fix that is to keep the thread suspended (wait for the restart
     * signal), and make sgen aware that even if a thread might be suspended, there
     * would be cases where you cannot scan its stack/registers. That would in fact
     * consist in removing the async suspend compensation, and treat the case directly
     * in sgen. That's also how it was done in the sgen specific suspend code.
     */

    /* thread_state_init_from_sigctx return FALSE if the current thread is starting or detaching and suspend can't continue. */
    current->suspend_can_continue = mono_threads_get_runtime_callbacks ()->thread_state_init_from_sigctx (&current->thread_saved_state [ASYNC_SUSPEND_STATE_INDEX], context);

    if (!current->suspend_can_continue)
            THREADS_SUSPEND_DEBUG ("\tThread is starting or detaching, failed to capture state %p\n", mono_thread_info_get_tid (current));

    /*
    Block the restart signal.
    We need to block the restart signal while posting to the suspend_ack semaphore or we race to sigsuspend,
    which might miss the signal and get stuck.
    */
    pthread_sigmask (SIG_BLOCK, &suspend_ack_signal_mask, NULL);

    /* We're done suspending */
    mono_threads_notify_initiator_of_suspend (current);

    THREADS_SUSPEND_DEBUG("[%p] suspend signal start");

    do {
        current->signal = 0;
        sigsuspend (&suspend_signal_mask);
    } while (current->signal != restart_signal_num);

    THREADS_SUSPEND_DEBUG("[%p] suspend signal end");

    /* Unblock the restart signal. */
    pthread_sigmask (SIG_UNBLOCK, &suspend_ack_signal_mask, NULL);

    if (current->async_target) {
#if MONO_ARCH_HAS_MONO_CONTEXT
        MonoContext tmp = current->thread_saved_state [ASYNC_SUSPEND_STATE_INDEX].ctx;
        mono_threads_get_runtime_callbacks ()->setup_async_callback (&tmp, current->async_target, current->user_data);
        current->user_data = NULL;
        current->async_target = NULL;
        mono_monoctx_to_sigctx (&tmp, context);
#else
        g_error ("The new interruption machinery requires a working mono-context");
#endif
    }

    /* We're done resuming */
    mono_threads_notify_initiator_of_resume (current);

    done:
    (void*)0;
}

gboolean mono_is_registered_thread()
{
    return mono_thread_info_get_small_id() != -1;
}

void
melonloader_signal_handler_fallback(t_merged_signal* merged, int _dummy, siginfo_t* info, void* context)
{
    THREADS_SUSPEND_DEBUG("[%p] [%d] signal fallback", mono_native_thread_id_get(), info->si_signo);
    if (merged->original->sa_sigaction) {
        merged->original->sa_sigaction(_dummy, info, context);
    }
}

void
ml_v2_signal_generic_handler(int _dummy, siginfo_t* info, void* context)
{
    int old_errno = errno;

    if (!g_hash_table_contains(mono_ml_signal_handler_patched, (gconstpointer) info->si_signo))
        g_assert("signal should not be handled");

    t_merged_signal* merged = g_hash_table_lookup(mono_ml_signal_handler_patched, (gconstpointer) info->si_signo);

    auto isRegistered = mono_is_registered_thread();

    if (!isRegistered) {
        THREADS_SUSPEND_DEBUG("[%p] [%d] assuming signal from il2cpp", mono_native_thread_id_get(), info->si_signo);

        melonloader_signal_handler_fallback(merged, _dummy, info, context);
        goto done;
    }

    THREADS_SUSPEND_DEBUG("[%p] [%d] is registered thread", mono_native_thread_id_get(), info->si_signo);

    int hp_save_index;
    gboolean shouldSaveHandler = info->si_signo == suspend_signal_num;

    if (shouldSaveHandler)
        hp_save_index = mono_hazard_pointer_save_for_signal_handler ();

    MonoThreadInfo *current = mono_thread_info_current ();
    gboolean isSignalAuthority = !current->suspend_source_locked;

    if (isSignalAuthority)
        current->suspend_source_locked = TRUE;

    switch (current->suspend_source) {
        case MONO_THREAD_SUSPEND_SOURCE_EXTERNAL:
            THREADS_SUSPEND_DEBUG("[%p] [%d] is registered, but external call assumed", mono_native_thread_id_get(), info->si_signo);
            melonloader_signal_handler_fallback(merged, _dummy, info, context);
            goto done_registered;
    }

    g_assert(merged->new->sa_sigaction == suspend_signal_handler || merged->new->sa_sigaction == restart_signal_handler);

    THREADS_SUSPEND_DEBUG("[%p] [%d] calling managed signal", mono_native_thread_id_get(), info->si_signo);

    merged->new->sa_sigaction(_dummy, info, context);

done_registered:
    if (isSignalAuthority) {
        current->suspend_source = MONO_THREAD_SUSPEND_SOURCE_DEFAULT;
        current->suspend_source_locked = FALSE;
        THREADS_SUSPEND_DEBUG("[%p] [%d] reset signal source", mono_native_thread_id_get(), info->si_signo);
    } else {
        THREADS_SUSPEND_DEBUG("[%p] [%d] skipping resetting suspend source - not authority", mono_native_thread_id_get(), info->si_signo);
    }

    if (shouldSaveHandler)
        mono_hazard_pointer_restore_for_signal_handler (hp_save_index);

done:
    mono_set_errno (old_errno);
}

void
ml_signal_generic_handler(int _dummy, siginfo_t* info, void* context)
{
    int hp_save_index = -1;
    int old_errno = errno;
    MonoThreadInfo *current = NULL;

    if (!g_hash_table_contains(mono_ml_signal_handler_patched, (gconstpointer) info->si_signo))
        g_assert("signal should not be handled");

    t_merged_signal* merged = g_hash_table_lookup(mono_ml_signal_handler_patched, (gconstpointer) info->si_signo);

    int small_id = mono_thread_info_get_small_id();
    MonoNativeThreadId native_tid = mono_native_thread_id_get ();

    gboolean unknown_thread = small_id < 0;
    gboolean managed_externally = !mono_il2cpp_check_current_thread(native_tid);

    if (managed_externally && unknown_thread) {
        il2cpp_end:

        g_assert(merged->original->sa_sigaction != suspend_signal_handler);
        g_assert(merged->original->sa_sigaction != restart_signal_handler);
        THREADS_SUSPEND_DEBUG("%p suspending il2cpp", info->si_signo);
        merged->original->sa_sigaction(_dummy, info, context);

        return;
    }

    g_assert(merged->new->sa_sigaction == suspend_signal_handler || merged->new->sa_sigaction == restart_signal_handler);

    THREADS_SUSPEND_DEBUG("%p suspending mono", info->si_signo);
    merged->new->sa_sigaction(_dummy, info, context);

    current = mono_thread_info_lookup (native_tid);

    MonoNativeThreadId main_thread_tid;
    gboolean isMain = mono_native_thread_id_main_thread_known (&main_thread_tid) &&
                      mono_native_thread_id_equals (mono_thread_info_get_tid(current), main_thread_tid);

    int currentState = mono_thread_info_current_state(current);
    if (currentState == STATE_RUNNING) {
        goto il2cpp_end;
    }

    mono_end:
    THREADS_SUSPEND_DEBUG("%p suspending mono", info->si_signo);
    merged->new->sa_sigaction(_dummy, info, context);

    goto done1;

    done1:

    mono_hazard_pointer_restore_for_signal_handler (hp_save_index);
    mono_set_errno (old_errno);
}

static void
signal_add_handler (int signo, void (*handler)(int, siginfo_t *, void *), int flags)
{
	struct sigaction* managed_sa = malloc(sizeof(struct sigaction));
	struct sigaction* old_sa = malloc(sizeof(struct sigaction));
	int ret;

	managed_sa->sa_sigaction = handler;
	sigfillset (&managed_sa->sa_mask);
	managed_sa->sa_flags = SA_SIGINFO | flags;

	ret = sigaction (signo, managed_sa, old_sa);
	g_assert (ret != -1);

	t_merged_signal* merged = NULL;
	
	if (!g_hash_table_contains(mono_ml_signal_handler_patched, (gconstpointer) signo)) {
		merged = malloc(sizeof(t_merged_signal));
		merged->original = old_sa;

		g_hash_table_insert(mono_ml_signal_handler_patched, (gpointer) signo, merged);
	} else {
		merged = (t_merged_signal*)g_hash_table_lookup(mono_ml_signal_handler_patched, (gconstpointer) signo);
		THREADS_SUSPEND_DEBUG("[%p] handler already loader", signo);
	}

	merged->new = managed_sa;

	struct sigaction sa;
	sa.sa_sigaction = ml_v2_signal_generic_handler;
	sigfillset(&sa.sa_mask);
	sa.sa_flags = managed_sa->sa_flags | old_sa->sa_flags;

	ret = sigaction(signo, &sa, NULL);
	g_assert(ret != -1);
}

static int
abort_signal_get (void)
{
#if defined(HOST_ANDROID)
	return SIGTTIN;
#elif defined (__OpenBSD__)
	return SIGUSR1;
#elif defined (SIGRTMIN)
	static int abort_signum = -1;
	if (abort_signum == -1)
		abort_signum = mono_threads_suspend_search_alternative_signal ();
	return abort_signum;
#elif defined (SIGTTIN)
	return SIGTTIN;
#else
	g_error ("unable to get abort signal");
#endif
}

static int
suspend_signal_get (void)
{
#if defined(HOST_ANDROID)
	return SIGPWR;
#elif defined (SIGRTMIN)
	static int suspend_signum = -1;
	if (suspend_signum == -1)
		suspend_signum = mono_threads_suspend_search_alternative_signal ();
	return suspend_signum;
#else
#if defined(__APPLE__) || defined(__OpenBSD__) || defined(__FreeBSD__) || defined(__FreeBSD_kernel__)
	return SIGXFSZ;
#else
	return SIGPWR;
#endif
#endif
}

static int
restart_signal_get (void)
{
#if defined(HOST_ANDROID)
	return SIGXCPU;
#elif defined (SIGRTMIN)
	static int restart_signum = -1;
	if (restart_signum == -1)
		restart_signum = mono_threads_suspend_search_alternative_signal ();
	return restart_signum;
#else
	return SIGXCPU;
#endif
}

void
mono_threads_suspend_init_signals (void)
{
	sigset_t signal_set;

	if (mono_ml_signal_handler_patched == null) {
		THREADS_SUSPEND_DEBUG("adding new hash table");
		mono_ml_signal_handler_patched = g_hash_table_new(g_direct_hash, g_direct_equal);
	}

	sigemptyset (&signal_set);

	/* add suspend signal */
	suspend_signal_num = suspend_signal_get ();

	signal_add_handler (suspend_signal_num, suspend_signal_handler, SA_RESTART);

	sigaddset (&signal_set, suspend_signal_num);

	/* add restart signal */
	restart_signal_num = restart_signal_get ();

	sigfillset (&suspend_signal_mask);
	sigdelset (&suspend_signal_mask, restart_signal_num);

	sigemptyset (&suspend_ack_signal_mask);
	sigaddset (&suspend_ack_signal_mask, restart_signal_num);

	signal_add_handler (restart_signal_num, restart_signal_handler, SA_RESTART);

	sigaddset (&signal_set, restart_signal_num);

	/* add abort signal */
	abort_signal_num = abort_signal_get ();

	/* the difference between abort and suspend here is made by not
	 * passing SA_RESTART, meaning we won't restart the syscall when
	 * receiving a signal */
	signal_add_handler (abort_signal_num, suspend_signal_handler, 0);

	sigaddset (&signal_set, abort_signal_num);

	/* ensure all the new signals are unblocked */
	sigprocmask (SIG_UNBLOCK, &signal_set, NULL);

	/*
	On 32bits arm Android, signals with values >=32 are not usable as their headers ship a broken sigset_t.
	See 5005c6f3fbc1da584c6a550281689cc23f59fe6d for more details.
	*/
#ifdef HOST_ANDROID
	g_assert (suspend_signal_num < 32);
	g_assert (restart_signal_num < 32);
	g_assert (abort_signal_num < 32);
#endif
}

void mono_melonloader_thread_suspend_reload() {
	THREADS_SUSPEND_DEBUG("reloading");

	signal_add_handler(suspend_signal_num, suspend_signal_handler, SA_RESTART);
	signal_add_handler(restart_signal_num, restart_signal_handler, SA_RESTART);
	signal_add_handler(abort_signal_num, suspend_signal_handler, 0);
}

gint
mono_threads_suspend_get_suspend_signal (void)
{
	g_assert (suspend_signal_num != -1);
	return suspend_signal_num;
}

gint
mono_threads_suspend_get_restart_signal (void)
{
	g_assert (restart_signal_num != -1);
	return restart_signal_num;
}

gint
mono_threads_suspend_get_abort_signal (void)
{
	g_assert (abort_signal_num != -1);
	return abort_signal_num;
}

#else

#include <mono/utils/mono-compiler.h>

MONO_EMPTY_SOURCE_FILE (mono_threads_posix_signals);

#endif /* defined(USE_POSIX_BACKEND) */
