-- W3C N-Quads storage - a single table of (subject, predicate, object, graph) statements.
-- No multi-tenancy fields yet (deliberately deferred - see this library's own docs/notes);
-- the "graph" column is where tenant/org/user scoping will eventually be encoded, matching
-- the nquad-end-to-end-poc's convention (e.g. a per-user default graph IRI).
CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS n_quads (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    subject TEXT NOT NULL,
    predicate TEXT NOT NULL,
    object TEXT NOT NULL,
    graph TEXT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_n_quads_subject ON n_quads (subject);
CREATE INDEX IF NOT EXISTS ix_n_quads_predicate ON n_quads (predicate);
CREATE INDEX IF NOT EXISTS ix_n_quads_graph ON n_quads (graph);


-- Forward lookup (subject -> its predicates) and reverse lookup (predicate + object -> subjects, e.g. every child of a
-- parent, or every entity of a type). object can be an arbitrarily long literal, so only its first 255 characters are
-- indexed; queries must repeat the left(object, 255) equality so the planner can use the expression index
-- (NpgsqlNQuadStore.QueryAsync does).
CREATE INDEX IF NOT EXISTS ix_n_quads_subject_predicate ON n_quads (subject, predicate);
CREATE INDEX IF NOT EXISTS ix_n_quads_predicate_object ON n_quads (predicate, left(object, 255));
