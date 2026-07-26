// Versioned C ABI entry point for structured build diagnostics.

fgcad_status fgcad_document_get_build_metrics(
	fgcad_document* document,
	const char* owner_id,
	fgcad_build_metrics* metrics)
{
	return not_found_guarded([&]()
	{
		if (document == nullptr || metrics == nullptr)
		{
			throw std::invalid_argument("Build metrics arguments cannot be null.");
		}

		auto found = document->build_metrics.find(require_text(owner_id, "owner_id"));
		if (found == document->build_metrics.end())
		{
			throw std::out_of_range("No build metrics are available for the requested owner.");
		}
		*metrics = found->second;
		return FGCAD_STATUS_OK;
	});
}
