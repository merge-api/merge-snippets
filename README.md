# merge-snippets

Snippets for Implementing Merge!

# Merge Internal Snippets

A curated set of **internal** code snippets created during Services and implementation projects.

The goal is to make Merge API code snippet patterns **easy to find, safe to reuse, and consistent across languages**.

## What belongs here

- Reusable implementation patterns (Link, auth, common model reads/writes, webhooks, passthrough)
- Minimal, focused examples (prefer a small snippet over a full demo app)
- Sanitized fixtures and sample payloads (no real customer data)

## Core principles

- **Workflow-first organization:** Browse by what you need to do, then pick a language.
- **Reusable by default:** Everything under `snippets/` should be customer-agnostic
- **No secrets, ever:** Use `.env.example`. Never commit credentials or tokens.
- **One folder = one purpose:** Keep examples small and easy to lift into a project.

## Repository Layout

```
merge-snippets/
├── README.md
├── .github/
│   ├── workflows/
│   └── CODEOWNERS
├── snippets/                      # canonical, reusable snippets
│   ├── link/                      # frontend: open Merge Link, callbacks, etc.
│   ├── auth/                      # backend: public_token -> account_token exchange, storage patterns
│   ├── common-models/
│   │   ├── hris/
│   │   ├── ats/
│   │   ├── accounting/
│   │   └── filestorage/
│   ├── webhooks/                  # processing, retries, idempotency, signature verification
│   ├── passthrough/               # code snippets for direct-to-third-party examples
│   └── field-mappings/            # field mapping / customization patterns
├── playbooks/                     # future: end-to-end guides that stitch snippets together
│   ├── hris-implementation/
│   └── sharepoint-implementation/
├── customers/                     # OPTIONAL: customer-specific code (use sparingly)
│   └── <customer-slug>/
└── shared/
    └── fixtures/                  # optional: sanitized example payloads/responses
```

### Where should “language” go?

For canonical snippets, place language **after** the workflow/category/use-case folder:

- **workflow / category / model-or-endpoint-or-use-case / language**

This keeps the “what” centralized (one place to document the pattern), with multiple language implementations side-by-side.

Example:

```
snippets/common-models/hris/employee-payroll-runs/
├── README.md
├── python/
│   └── list_payroll_runs.py
├── node/
│   └── listPayrollRuns.ts
├── java/
│   └── ListPayrollRuns.java
└── csharp/
    └── ListPayrollRuns.cs
```

This keeps the “Payroll Runs” pattern centralized, with all languages side-by-side.

## Snippet Folder Standard

Each snippet should be a folder containing:

- [`README.md`](http://README.md)
- One or more snippet files
- Optional sanitized fixtures under `fixtures/`

### Snippet README Template (copy/paste)

```markdown
# <Snippet name>

## Purpose

What this snippet does in 1–2 sentences.

## When to use

When you would reach for this during an implementation.

## Merge surface area

- Category:
- Endpoint(s):
- Models involved:

## Requirements

- SDK vs raw HTTP:
- Env vars required:
  - MERGE_API_KEY
  - MERGE_ACCOUNT_TOKEN
  - (etc.)

## How to run

Step-by-step instructions.

## Expected output

What a successful run looks like.

## Notes / gotchas

Pagination, rate limits, expands, retries, etc.

## Security

- Never commit secrets.
- Never log sensitive tokens.
- Fixtures must be sanitized (no customer data).
```

## Security and Hygiene

- Do not commit:
  - API keys, account tokens, OAuth client secrets, webhook signing secrets
  - Customer names, domains, IDs, or payloads that can identify a customer
- Use:
  - `.env.example` for configuration
  - `shared/fixtures/` or snippet-local `fixtures/` for sanitized payloads
- Prefer review protection for:
  - `customers/**`
  - `.github/**`
  - dependency manifests

## Customer-Specific Work (optional)

If you choose to keep customer-specific snippets, isolate them under:

```
customers/<customer-slug>/
```

Rules:

- Keep customer work minimal and time-bound.
- Add clear notes on what is redacted.
- When something becomes reusable, promote it into `snippets/` and delete/trim the customer copy.

## Branch Naming

Avoid leading slashes in branch names. Use:

- `snippets/<area>/<purpose>`
- `playbooks/<area>/<purpose>`
- `customers/<customer-slug>/<purpose>`

Examples:

- `snippets/webhooks/retry-handling`
- `customers/customer-name/passthrough-examples`
