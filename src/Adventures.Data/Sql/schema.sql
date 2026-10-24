-- JSONB Hybrid + W3C N-Quads schema for Adventures.Data
-- Apply manually against the local Docker Postgres instance (or via a future migration tool).

CREATE TABLE IF NOT EXISTS entities (
	id               UUID PRIMARY KEY,
	tenant           TEXT NOT NULL,
	org              TEXT NOT NULL,
	entity_type      TEXT NOT NULL,
	standard_fields  JSONB NOT NULL DEFAULT '{}'::jsonb,
	created_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
	updated_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
	row_version      BIGINT NOT NULL DEFAULT 1
);

CREATE INDEX IF NOT EXISTS ix_entities_tenant_org_type ON entities (tenant, org, entity_type);
CREATE INDEX IF NOT EXISTS ix_entities_standard_fields_gin ON entities USING GIN (standard_fields);

-- N-Quads: subject-predicate-object-graph statements for user-defined/extension fields.
-- "graph" encodes the scoping context, e.g. 'tenant:acme.com/org:my-blog/user:42'.
CREATE TABLE IF NOT EXISTS entity_triples (
	subject          UUID NOT NULL REFERENCES entities (id) ON DELETE CASCADE,
	predicate        TEXT NOT NULL,
	object           TEXT NOT NULL,
	object_datatype  TEXT NOT NULL DEFAULT 'xsd:string',
	graph            TEXT NOT NULL,
	PRIMARY KEY (subject, predicate, graph)
);

CREATE INDEX IF NOT EXISTS ix_entity_triples_subject_graph ON entity_triples (subject, graph);
