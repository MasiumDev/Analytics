export default function Home() {
  return (
    <main className="flex min-h-screen items-center justify-center bg-slate-50 px-6 text-slate-950">
      <section className="w-full max-w-2xl rounded-3xl border border-slate-200 bg-white p-10 shadow-sm">
        <p className="text-sm font-medium uppercase tracking-[0.2em] text-slate-500">
          Development scaffold
        </p>
        <h1 className="mt-3 text-4xl font-semibold tracking-tight">
          Workspace ready
        </h1>
        <p className="mt-4 max-w-xl text-lg leading-8 text-slate-600">
          The frontend and API foundations are ready for the next product
          increment.
        </p>
      </section>
    </main>
  );
}
