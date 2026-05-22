$ErrorActionPreference = 'Stop'

$hmiRoot = 'C:\Users\carlo\source\repos\hmi_wise_DDG\WISE\src'

function Write-Utf8File {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content
    )

    $directory = Split-Path -Parent $Path
    if (-not (Test-Path $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    Set-Content -Path $Path -Value $Content -Encoding UTF8
}

function Replace-Once {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$OldValue,
        [Parameter(Mandatory = $true)][string]$NewValue
    )

    $content = Get-Content -Path $Path -Raw
    if ($content.Contains($NewValue)) {
        return
    }

    if (-not $content.Contains($OldValue)) {
        throw "Unable to find replacement marker in $Path"
    }

    $updated = $content.Replace($OldValue, $NewValue)
    Set-Content -Path $Path -Value $updated -Encoding UTF8
}

$productionDesignPanelContent = @'
import { useEffect, useMemo, useState } from 'react';
import { createProductionDesign, listDocuments } from '../services/api';
import { processLabel } from '../utils/format';
import './ProductionDesignPanel.css';

const USE_CASE_LABELS = {
  blade: 'Blade',
  prosthesis: 'Prosthesis',
  patch: 'Patch',
};

const getBaseProcessType = (name) => {
  if (!name) return '';
  const upper = name.toUpperCase();
  if (upper.includes('DED')) return 'DED';
  if (upper.includes('DALP')) return 'DALP';
  if (upper.includes('NS')) return 'NS';
  if (upper.includes('FS')) return 'FS';
  if (upper.includes('2PP')) return '2PP';
  return 'Manual';
};

const requiresMachineExecution = (processName) => getBaseProcessType(processName) !== 'Manual';

const buildDefaultDesignName = (config, individual) => {
  const stamp = new Date().toISOString().slice(0, 16).replace('T', ' ');
  const subject = individual || config.baseName;
  return `${subject} ${stamp}`;
};

export default function ProductionDesignPanel({
  config,
  username,
  individual,
  displayedProcessChain,
  processParameters,
}) {
  const [designName, setDesignName] = useState(() => buildDefaultDesignName(config, individual));
  const [notes, setNotes] = useState('');
  const [selectedPartPrograms, setSelectedPartPrograms] = useState({});
  const [partPrograms, setPartPrograms] = useState([]);
  const [isLoadingDocuments, setIsLoadingDocuments] = useState(false);
  const [isSaving, setIsSaving] = useState(false);
  const [feedback, setFeedback] = useState(null);

  const useCase = USE_CASE_LABELS[config.ontologyIdentifier] || 'General';

  useEffect(() => {
    setDesignName(buildDefaultDesignName(config, individual));
  }, [config.baseName, individual]);

  useEffect(() => {
    const nextSelections = {};
    (displayedProcessChain || []).forEach((process) => {
      nextSelections[process.name] = selectedPartPrograms[process.name] || '';
    });
    setSelectedPartPrograms(nextSelections);
  }, [displayedProcessChain]);

  useEffect(() => {
    let isMounted = true;

    async function loadPartPrograms() {
      setIsLoadingDocuments(true);
      try {
        const documents = await listDocuments({
          useCase,
          category: 'Part Program',
        });

        if (isMounted) {
          setPartPrograms(documents);
        }
      } catch (error) {
        if (isMounted) {
          setFeedback({ type: 'error', message: `Failed to load part programs: ${error.message}` });
        }
      } finally {
        if (isMounted) {
          setIsLoadingDocuments(false);
        }
      }
    }

    loadPartPrograms();
    return () => {
      isMounted = false;
    };
  }, [useCase]);

  const partProgramById = useMemo(
    () => Object.fromEntries(partPrograms.map((document) => [document.documentId, document])),
    [partPrograms]
  );

  const executableSteps = useMemo(
    () => (displayedProcessChain || []).filter((process) => requiresMachineExecution(process.name)),
    [displayedProcessChain]
  );

  const handleSaveDesign = async () => {
    if (!designName.trim()) {
      setFeedback({ type: 'error', message: 'Design name is required.' });
      return;
    }

    const missingPrograms = executableSteps.filter((process) => !selectedPartPrograms[process.name]);
    if (missingPrograms.length > 0) {
      setFeedback({
        type: 'error',
        message: `Link a part program to every machine step before saving. Missing: ${missingPrograms.map((item) => processLabel(item.name)).join(', ')}.`,
      });
      return;
    }

    setIsSaving(true);
    setFeedback(null);

    try {
      const processSteps = (displayedProcessChain || []).map((process, index) => {
        const processState = processParameters[process.name] || { parameters: {}, selectedRecipeIdx: 0 };
        const selectedRecipe = process.recipes?.[processState.selectedRecipeIdx];
        const linkedDocumentId = selectedPartPrograms[process.name] || null;
        const linkedDocument = linkedDocumentId ? partProgramById[linkedDocumentId] : null;

        return {
          sequence: index + 1,
          processName: process.name,
          processLabel: processLabel(process.name),
          requiresMachineExecution: requiresMachineExecution(process.name),
          selectedRecipeName: selectedRecipe?.id || null,
          selectedRecipeIndex: processState.selectedRecipeIdx ?? 0,
          partProgramDocumentId: linkedDocumentId,
          partProgramFileName: linkedDocument?.originalFileName || null,
          supportingDocumentIds: [],
          parameters: Object.entries(processState.parameters || {}).map(([key, parameter]) => ({
            key,
            label: parameter.label || key,
            unit: parameter.unit || null,
            value: String(parameter.value ?? ''),
          })),
        };
      });

      const relatedDocuments = processSteps
        .filter((step) => step.partProgramDocumentId)
        .map((step) => ({
          documentId: step.partProgramDocumentId,
          role: `part-program-step-${step.sequence}`,
          category: 'Part Program',
          fileName: step.partProgramFileName,
        }));

      const design = await createProductionDesign({
        designName: designName.trim(),
        useCase,
        productIdentifier: individual || config.productId,
        designedBy: username,
        notes: notes.trim() || null,
        relatedDocuments,
        processSteps,
      });

      setFeedback({
        type: 'success',
        message: `Design saved successfully with id ${design.designId}.`,
      });
    } catch (error) {
      setFeedback({ type: 'error', message: `Failed to save design: ${error.message}` });
    } finally {
      setIsSaving(false);
    }
  };

  return (
    <div className="card glass-morphism production-design-card">
      <div className="card-header">
        <img src="/icons/generate.svg" alt="Production design" className="header-icon" />
        <h3>Production Design</h3>
      </div>
      <p className="card-description">
        Snapshot the configured process chain, parameter values and linked part programs into a
        production-ready design that the ProcessCoordinator can execute later.
      </p>

      <div className="production-design-form">
        <label>
          Design Name
          <input
            type="text"
            value={designName}
            onChange={(event) => setDesignName(event.target.value)}
            placeholder="Name this configured design"
          />
        </label>

        <label>
          Notes
          <textarea
            rows="3"
            value={notes}
            onChange={(event) => setNotes(event.target.value)}
            placeholder="Optional notes about the design or production intent"
          />
        </label>
      </div>

      <div className="production-design-links">
        <div className="production-design-links-header">
          <h4>Step-to-GCode Mapping</h4>
          <span>{isLoadingDocuments ? 'Refreshing part programs...' : `${partPrograms.length} part program(s) available`}</span>
        </div>

        <div className="production-design-steps">
          {(displayedProcessChain || []).map((process, index) => {
            const processState = processParameters[process.name] || { selectedRecipeIdx: 0 };
            const selectedRecipe = process.recipes?.[processState.selectedRecipeIdx];
            const isExecutable = requiresMachineExecution(process.name);

            return (
              <div key={process.name} className="production-design-step-row">
                <div className="production-design-step-meta">
                  <span className="production-design-step-number">{index + 1}</span>
                  <div>
                    <strong>{processLabel(process.name)}</strong>
                    <div className="production-design-step-subtitle">
                      {selectedRecipe?.id ? `Recipe: ${selectedRecipe.id}` : 'No recipe preset selected'}
                    </div>
                  </div>
                </div>

                {isExecutable ? (
                  <select
                    value={selectedPartPrograms[process.name] || ''}
                    onChange={(event) =>
                      setSelectedPartPrograms((current) => ({
                        ...current,
                        [process.name]: event.target.value,
                      }))
                    }
                  >
                    <option value="">Select linked part program...</option>
                    {partPrograms.map((document) => (
                      <option key={document.documentId} value={document.documentId}>
                        {document.originalFileName}
                      </option>
                    ))}
                  </select>
                ) : (
                  <div className="production-design-manual-step">Manual or non-machine step</div>
                )}
              </div>
            );
          })}
        </div>
      </div>

      <div className="production-design-actions">
        <button className="action-btn submit-btn" onClick={handleSaveDesign} disabled={isSaving}>
          {isSaving ? 'Saving design...' : 'Save Production Design'}
        </button>
      </div>

      {feedback && (
        <div className={`production-design-feedback ${feedback.type}`}>
          {feedback.message}
        </div>
      )}
    </div>
  );
}
'@

$productionDesignPanelCss = @'
.production-design-card {
  margin-top: 24px;
}

.production-design-form {
  display: grid;
  gap: 16px;
}

.production-design-form label {
  display: grid;
  gap: 8px;
  color: #243248;
  font-weight: 600;
}

.production-design-form input,
.production-design-form textarea,
.production-design-step-row select {
  width: 100%;
  border: 1px solid rgba(117, 138, 173, 0.35);
  border-radius: 16px;
  padding: 12px 14px;
  font: inherit;
  color: #243248;
  background: rgba(255, 255, 255, 0.9);
  box-sizing: border-box;
}

.production-design-form textarea {
  resize: vertical;
}

.production-design-links {
  margin-top: 20px;
}

.production-design-links-header {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  gap: 12px;
  margin-bottom: 12px;
}

.production-design-links-header h4 {
  margin: 0;
  color: #243248;
}

.production-design-links-header span {
  color: #66758e;
  font-size: 0.95rem;
}

.production-design-steps {
  display: grid;
  gap: 12px;
}

.production-design-step-row {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(260px, 360px);
  gap: 16px;
  align-items: center;
  padding: 14px 16px;
  border-radius: 18px;
  background: rgba(236, 243, 252, 0.72);
}

.production-design-step-meta {
  display: flex;
  align-items: center;
  gap: 14px;
}

.production-design-step-number {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 32px;
  height: 32px;
  border-radius: 50%;
  background: #1d4ed8;
  color: white;
  font-weight: 700;
}

.production-design-step-subtitle {
  color: #66758e;
  font-size: 0.92rem;
  margin-top: 4px;
}

.production-design-manual-step {
  padding: 12px 14px;
  border-radius: 16px;
  background: rgba(255, 255, 255, 0.78);
  color: #66758e;
  text-align: center;
}

.production-design-actions {
  margin-top: 20px;
  display: flex;
  justify-content: flex-end;
}

.production-design-feedback {
  margin-top: 16px;
  padding: 14px 16px;
  border-radius: 16px;
  font-weight: 600;
}

.production-design-feedback.success {
  background: rgba(21, 128, 61, 0.14);
  color: #166534;
}

.production-design-feedback.error {
  background: rgba(220, 38, 38, 0.14);
  color: #b91c1c;
}

@media (max-width: 900px) {
  .production-design-step-row {
    grid-template-columns: 1fr;
  }

  .production-design-actions {
    justify-content: stretch;
  }

  .production-design-actions .action-btn {
    width: 100%;
  }
}
'@

$productionManagementContent = @'
import { useEffect, useMemo, useState } from 'react';
import Header from './Header';
import './shared.css';
import './ProductionManagement.css';
import {
  getProductionDesign,
  listProductionDesigns,
  listProductionJobs,
  queueProductionJob,
} from '../services/api';
import { processLabel } from '../utils/format';

const USE_CASE_OPTIONS = ['', 'Blade', 'Prosthesis', 'Patch', 'General'];

const formatDate = (value) => {
  if (!value) return '-';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
};

const toUtcBoundary = (value, endOfDay = false) => {
  if (!value) return '';
  return endOfDay
    ? new Date(`${value}T23:59:59.999`).toISOString()
    : new Date(`${value}T00:00:00.000`).toISOString();
};

export default function ProductionManagement() {
  const storedData = JSON.parse(sessionStorage.getItem('dashboardData'));
  const username = storedData?.username || 'Guest';

  const [designs, setDesigns] = useState([]);
  const [jobs, setJobs] = useState([]);
  const [selectedDesignId, setSelectedDesignId] = useState('');
  const [selectedDesign, setSelectedDesign] = useState(null);
  const [search, setSearch] = useState('');
  const [useCase, setUseCase] = useState('');
  const [fromDate, setFromDate] = useState('');
  const [toDate, setToDate] = useState('');
  const [isLoading, setIsLoading] = useState(true);
  const [isLaunching, setIsLaunching] = useState(false);
  const [feedback, setFeedback] = useState(null);

  const filters = useMemo(() => ({
    search: search.trim(),
    useCase: useCase.trim(),
    fromUtc: fromDate ? toUtcBoundary(fromDate, false) : '',
    toUtc: toDate ? toUtcBoundary(toDate, true) : '',
  }), [search, useCase, fromDate, toDate]);

  const loadDesigns = async (activeFilters = filters) => {
    setIsLoading(true);
    try {
      const items = await listProductionDesigns(activeFilters);
      setDesigns(items);
      if (items.length > 0 && !selectedDesignId) {
        setSelectedDesignId(items[0].designId);
      }
      if (items.length === 0) {
        setSelectedDesign(null);
      }
    } catch (error) {
      setFeedback({ type: 'error', message: `Failed to load designs: ${error.message}` });
    } finally {
      setIsLoading(false);
    }
  };

  const loadJobs = async () => {
    try {
      const items = await listProductionJobs({});
      setJobs(items);
    } catch (error) {
      setFeedback({ type: 'error', message: `Failed to load jobs: ${error.message}` });
    }
  };

  useEffect(() => {
    loadDesigns(filters);
    loadJobs();
  }, []);

  useEffect(() => {
    if (!selectedDesignId) {
      setSelectedDesign(null);
      return;
    }

    let isMounted = true;
    getProductionDesign(selectedDesignId)
      .then((design) => {
        if (isMounted) {
          setSelectedDesign(design);
        }
      })
      .catch((error) => {
        if (isMounted) {
          setFeedback({ type: 'error', message: `Failed to load design details: ${error.message}` });
        }
      });

    return () => {
      isMounted = false;
    };
  }, [selectedDesignId]);

  const handleLaunch = async () => {
    if (!selectedDesignId) {
      setFeedback({ type: 'error', message: 'Select a design before launching production.' });
      return;
    }

    setIsLaunching(true);
    setFeedback(null);

    try {
      const job = await queueProductionJob({
        designId: selectedDesignId,
        requestedBy: username,
      });

      setFeedback({
        type: 'success',
        message: `Production job ${job.jobId} queued successfully.`,
      });

      await loadJobs();
    } catch (error) {
      setFeedback({ type: 'error', message: `Failed to queue production job: ${error.message}` });
    } finally {
      setIsLaunching(false);
    }
  };

  return (
    <div className="app-container production-management-page">
      <Header username={username} />

      <main className="main-content production-management-main">
        <div className="production-management-content">
          <section className="production-management-hero">
            <div>
              <h1>Production Management</h1>
              <p>
                Retrieve saved designs, inspect their machine-ready process sequence and queue a
                persisted production job for the ProcessCoordinator.
              </p>
            </div>
            <div className="production-management-status">
              {isLoading ? 'Refreshing designs...' : `${designs.length} design(s) available`}
            </div>
          </section>

          {feedback && (
            <div className={`production-management-feedback ${feedback.type}`}>
              {feedback.message}
            </div>
          )}

          <section className="production-management-grid">
            <article className="production-management-card">
              <div className="production-management-card-header">
                <div>
                  <h2>Saved Designs</h2>
                  <p>Search by name, use case and design date.</p>
                </div>
              </div>

              <form
                className="production-management-filters"
                onSubmit={async (event) => {
                  event.preventDefault();
                  await loadDesigns(filters);
                }}
              >
                <input
                  type="text"
                  value={search}
                  onChange={(event) => setSearch(event.target.value)}
                  placeholder="Search design or product"
                />
                <select value={useCase} onChange={(event) => setUseCase(event.target.value)}>
                  {USE_CASE_OPTIONS.map((option) => (
                    <option key={option || 'all'} value={option}>
                      {option || 'All use cases'}
                    </option>
                  ))}
                </select>
                <input type="date" value={fromDate} onChange={(event) => setFromDate(event.target.value)} />
                <input type="date" value={toDate} onChange={(event) => setToDate(event.target.value)} />
                <button className="document-button primary" type="submit">Search</button>
                <button
                  className="document-button secondary"
                  type="button"
                  onClick={async () => {
                    setSearch('');
                    setUseCase('');
                    setFromDate('');
                    setToDate('');
                    await loadDesigns({
                      search: '',
                      useCase: '',
                      fromUtc: '',
                      toUtc: '',
                    });
                  }}
                >
                  Reset
                </button>
              </form>

              <div className="production-design-list">
                {designs.map((design) => (
                  <button
                    key={design.designId}
                    type="button"
                    className={`production-design-item ${design.designId === selectedDesignId ? 'selected' : ''}`}
                    onClick={() => setSelectedDesignId(design.designId)}
                  >
                    <div>
                      <strong>{design.designName}</strong>
                      <div>{design.productIdentifier}</div>
                    </div>
                    <div className="production-design-item-meta">
                      <span>{design.useCase}</span>
                      <span>{formatDate(design.designedAtUtc)}</span>
                    </div>
                  </button>
                ))}

                {!isLoading && designs.length === 0 && (
                  <div className="production-empty-state">No saved designs match the current filters.</div>
                )}
              </div>
            </article>

            <article className="production-management-card">
              <div className="production-management-card-header">
                <div>
                  <h2>Design Detail</h2>
                  <p>Review the exact persisted steps before queueing production.</p>
                </div>
                <button
                  className="document-button primary"
                  type="button"
                  disabled={!selectedDesignId || isLaunching}
                  onClick={handleLaunch}
                >
                  {isLaunching ? 'Queueing...' : 'Launch Production'}
                </button>
              </div>

              {selectedDesign ? (
                <div className="production-design-detail">
                  <div className="production-design-summary">
                    <div><strong>Name:</strong> {selectedDesign.designName}</div>
                    <div><strong>Use Case:</strong> {selectedDesign.useCase}</div>
                    <div><strong>Designed By:</strong> {selectedDesign.designedBy}</div>
                    <div><strong>Designed At:</strong> {formatDate(selectedDesign.designedAtUtc)}</div>
                    <div><strong>Notes:</strong> {selectedDesign.notes || '-'}</div>
                  </div>

                  <div className="production-step-table-wrapper">
                    <table className="production-step-table">
                      <thead>
                        <tr>
                          <th>Step</th>
                          <th>Process</th>
                          <th>Recipe</th>
                          <th>Execution</th>
                          <th>Part Program</th>
                        </tr>
                      </thead>
                      <tbody>
                        {selectedDesign.processSteps.map((step) => (
                          <tr key={`${step.sequence}-${step.processName}`}>
                            <td>{step.sequence}</td>
                            <td>{processLabel(step.processName)}</td>
                            <td>{step.selectedRecipeName || '-'}</td>
                            <td>{step.requiresMachineExecution ? 'Machine' : 'Manual'}</td>
                            <td>{step.partProgramFileName || '-'}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </div>
              ) : (
                <div className="production-empty-state">Select a design to inspect it.</div>
              )}
            </article>
          </section>

          <section className="production-management-card production-jobs-card">
            <div className="production-management-card-header">
              <div>
                <h2>Production Jobs</h2>
                <p>Persisted execution history from the ProcessCoordinator.</p>
              </div>
              <button className="document-button secondary" type="button" onClick={loadJobs}>
                Refresh Jobs
              </button>
            </div>

            <div className="production-step-table-wrapper">
              <table className="production-step-table">
                <thead>
                  <tr>
                    <th>Job Id</th>
                    <th>Design</th>
                    <th>Status</th>
                    <th>Current Step</th>
                    <th>Requested By</th>
                    <th>Requested At</th>
                    <th>Completed At</th>
                    <th>Failure</th>
                  </tr>
                </thead>
                <tbody>
                  {jobs.map((job) => (
                    <tr key={job.jobId}>
                      <td>{job.jobId}</td>
                      <td>{job.designName}</td>
                      <td>{job.status}</td>
                      <td>{job.currentStepName || '-'}</td>
                      <td>{job.requestedBy}</td>
                      <td>{formatDate(job.requestedAtUtc)}</td>
                      <td>{formatDate(job.completedAtUtc)}</td>
                      <td>{job.failureReason || '-'}</td>
                    </tr>
                  ))}

                  {jobs.length === 0 && (
                    <tr>
                      <td colSpan="8" className="production-empty-state">No production jobs persisted yet.</td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          </section>
        </div>
      </main>
    </div>
  );
}
'@

$productionManagementCss = @'
.production-management-main {
  padding: 32px;
}

.production-management-content {
  display: grid;
  gap: 24px;
}

.production-management-hero,
.production-management-card {
  background: rgba(255, 255, 255, 0.82);
  border: 1px solid rgba(194, 210, 233, 0.72);
  border-radius: 28px;
  padding: 28px 32px;
  box-shadow: 0 24px 60px rgba(29, 78, 216, 0.08);
}

.production-management-hero {
  display: flex;
  justify-content: space-between;
  gap: 24px;
  align-items: flex-start;
}

.production-management-hero h1,
.production-management-card h2 {
  margin: 0 0 8px;
  color: #243248;
}

.production-management-hero p,
.production-management-card p {
  margin: 0;
  color: #66758e;
}

.production-management-status {
  color: #1d4ed8;
  font-weight: 700;
}

.production-management-feedback {
  padding: 16px 18px;
  border-radius: 18px;
  font-weight: 600;
}

.production-management-feedback.success {
  background: rgba(21, 128, 61, 0.14);
  color: #166534;
}

.production-management-feedback.error {
  background: rgba(220, 38, 38, 0.14);
  color: #b91c1c;
}

.production-management-grid {
  display: grid;
  grid-template-columns: 380px minmax(0, 1fr);
  gap: 24px;
}

.production-management-card-header {
  display: flex;
  justify-content: space-between;
  gap: 20px;
  align-items: flex-start;
  margin-bottom: 18px;
}

.production-management-filters {
  display: grid;
  grid-template-columns: minmax(0, 1.4fr) repeat(4, minmax(0, 1fr));
  gap: 12px;
  margin-bottom: 18px;
}

.production-management-filters input,
.production-management-filters select {
  width: 100%;
  border: 1px solid rgba(117, 138, 173, 0.35);
  border-radius: 16px;
  padding: 12px 14px;
  font: inherit;
  background: rgba(255, 255, 255, 0.94);
  color: #243248;
  box-sizing: border-box;
}

.production-design-list {
  display: grid;
  gap: 10px;
  max-height: 560px;
  overflow: auto;
}

.production-design-item {
  border: 1px solid rgba(194, 210, 233, 0.72);
  border-radius: 18px;
  padding: 16px;
  background: rgba(240, 246, 255, 0.78);
  cursor: pointer;
  text-align: left;
  display: flex;
  justify-content: space-between;
  gap: 12px;
}

.production-design-item.selected {
  border-color: #1d4ed8;
  box-shadow: inset 0 0 0 1px rgba(29, 78, 216, 0.18);
}

.production-design-item strong {
  display: block;
  color: #243248;
}

.production-design-item-meta {
  display: grid;
  gap: 4px;
  justify-items: end;
  color: #66758e;
  font-size: 0.92rem;
}

.production-design-detail {
  display: grid;
  gap: 18px;
}

.production-design-summary {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px 18px;
  color: #243248;
}

.production-step-table-wrapper {
  overflow: auto;
  border-radius: 18px;
  border: 1px solid rgba(214, 223, 237, 0.8);
}

.production-step-table {
  width: 100%;
  border-collapse: collapse;
  min-width: 720px;
}

.production-step-table th,
.production-step-table td {
  padding: 12px 14px;
  border-bottom: 1px solid rgba(229, 231, 235, 0.8);
  text-align: left;
}

.production-step-table thead th {
  background: rgba(236, 243, 252, 0.94);
  color: #243248;
  position: sticky;
  top: 0;
}

.production-jobs-card {
  padding-top: 24px;
}

.production-empty-state {
  color: #66758e;
  text-align: center;
  padding: 18px;
}

@media (max-width: 1200px) {
  .production-management-grid {
    grid-template-columns: 1fr;
  }

  .production-design-summary {
    grid-template-columns: 1fr;
  }
}

@media (max-width: 900px) {
  .production-management-main {
    padding: 20px;
  }

  .production-management-hero,
  .production-management-card {
    padding: 22px;
  }

  .production-management-hero,
  .production-management-card-header {
    flex-direction: column;
  }

  .production-management-filters {
    grid-template-columns: 1fr;
  }
}
'@

Write-Utf8File -Path (Join-Path $hmiRoot 'components\ProductionDesignPanel.jsx') -Content $productionDesignPanelContent
Write-Utf8File -Path (Join-Path $hmiRoot 'components\ProductionDesignPanel.css') -Content $productionDesignPanelCss
Write-Utf8File -Path (Join-Path $hmiRoot 'components\ProductionManagement.jsx') -Content $productionManagementContent
Write-Utf8File -Path (Join-Path $hmiRoot 'components\ProductionManagement.css') -Content $productionManagementCss

Replace-Once -Path (Join-Path $hmiRoot 'App.jsx') `
    -OldValue "import SystemStatus from './components/SystemStatus';" `
    -NewValue "import SystemStatus from './components/SystemStatus';`r`nimport ProductionManagement from './components/ProductionManagement';"

Replace-Once -Path (Join-Path $hmiRoot 'App.jsx') `
    -OldValue '                    <Route path="/helicopter-pro" element={<ProtectedRoute><WorkInProgress /></ProtectedRoute>} />' `
    -NewValue "                    <Route`r`n                        path=`"/production-management`"`r`n                        element={<ProtectedRoute><ProductionManagement /></ProtectedRoute>}`r`n                    />`r`n                    <Route path=`"/helicopter-pro`" element={<ProtectedRoute><WorkInProgress /></ProtectedRoute>} />"

Replace-Once -Path (Join-Path $hmiRoot 'components\Header.jsx') `
    -OldValue "    { label: 'System Status', path: '/system-status' }," `
    -NewValue "    { label: 'System Status', path: '/system-status' },`r`n    { label: 'Production Management', path: '/production-management' },"

$apiAppend = @'

export async function listProductionDesigns(filters = {}) {
  const query = new URLSearchParams();

  if (filters.search) query.set('search', filters.search);
  if (filters.useCase) query.set('useCase', filters.useCase);
  if (filters.designedBy) query.set('designedBy', filters.designedBy);
  if (filters.fromUtc) query.set('fromUtc', filters.fromUtc);
  if (filters.toUtc) query.set('toUtc', filters.toUtc);

  const suffix = query.toString() ? `?${query.toString()}` : '';
  const res = await fetch(`${API_URL}/api/production/designs${suffix}`);
  if (!res.ok) throw new Error(await res.text());
  return res.json();
}

export async function getProductionDesign(designId) {
  const res = await fetch(`${API_URL}/api/production/designs/${encodeURIComponent(designId)}`);
  if (!res.ok) throw new Error(await res.text());
  return res.json();
}

export async function createProductionDesign(payload) {
  const res = await fetch(`${API_URL}/api/production/designs`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  });

  if (!res.ok) throw new Error(await res.text());
  return res.json();
}

export async function listProductionJobs(filters = {}) {
  const query = new URLSearchParams();

  if (filters.status) query.set('status', filters.status);
  if (filters.designId) query.set('designId', filters.designId);
  if (filters.useCase) query.set('useCase', filters.useCase);
  if (filters.requestedBy) query.set('requestedBy', filters.requestedBy);

  const suffix = query.toString() ? `?${query.toString()}` : '';
  const res = await fetch(`${API_URL}/api/production/jobs${suffix}`);
  if (!res.ok) throw new Error(await res.text());
  return res.json();
}

export async function queueProductionJob(payload) {
  const res = await fetch(`${API_URL}/api/production/jobs`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  });

  if (!res.ok) throw new Error(await res.text());
  return res.json();
}
'@

$apiPath = Join-Path $hmiRoot 'services\api.js'
$apiContent = Get-Content -Path $apiPath -Raw
if (-not $apiContent.Contains('export async function listProductionDesigns')) {
    Set-Content -Path $apiPath -Value ($apiContent + $apiAppend) -Encoding UTF8
}

$proxyAppend = @'

app.get('/api/production/designs', async (req, res) => {
    try {
        const query = new URLSearchParams();
        if (req.query.search) query.set('search', req.query.search);
        if (req.query.useCase) query.set('useCase', req.query.useCase);
        if (req.query.designedBy) query.set('designedBy', req.query.designedBy);
        if (req.query.fromUtc) query.set('fromUtc', req.query.fromUtc);
        if (req.query.toUtc) query.set('toUtc', req.query.toUtc);

        const suffix = query.toString() ? `?${query.toString()}` : '';
        const response = await fetch(`${MIDDLEWARE_API_URL}/api/production/designs${suffix}`);
        await relayJsonResponse(response, res);
    } catch (error) {
        console.error('Production designs proxy failed:', error);
        res.status(502).json({
            message: 'Failed to reach middleware production designs endpoint.',
            details: error.message
        });
    }
});

app.get('/api/production/designs/:designId', async (req, res) => {
    try {
        const response = await fetch(`${MIDDLEWARE_API_URL}/api/production/designs/${encodeURIComponent(req.params.designId)}`);
        await relayJsonResponse(response, res);
    } catch (error) {
        console.error('Production design detail proxy failed:', error);
        res.status(502).json({
            message: 'Failed to reach middleware production design detail endpoint.',
            details: error.message
        });
    }
});

app.post('/api/production/designs', async (req, res) => {
    try {
        const response = await fetch(`${MIDDLEWARE_API_URL}/api/production/designs`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(req.body)
        });

        await relayJsonResponse(response, res);
    } catch (error) {
        console.error('Production design create proxy failed:', error);
        res.status(502).json({
            message: 'Failed to reach middleware production design create endpoint.',
            details: error.message
        });
    }
});

app.get('/api/production/jobs', async (req, res) => {
    try {
        const query = new URLSearchParams();
        if (req.query.status) query.set('status', req.query.status);
        if (req.query.designId) query.set('designId', req.query.designId);
        if (req.query.useCase) query.set('useCase', req.query.useCase);
        if (req.query.requestedBy) query.set('requestedBy', req.query.requestedBy);

        const suffix = query.toString() ? `?${query.toString()}` : '';
        const response = await fetch(`${MIDDLEWARE_API_URL}/api/production/jobs${suffix}`);
        await relayJsonResponse(response, res);
    } catch (error) {
        console.error('Production jobs proxy failed:', error);
        res.status(502).json({
            message: 'Failed to reach middleware production jobs endpoint.',
            details: error.message
        });
    }
});

app.post('/api/production/jobs', async (req, res) => {
    try {
        const response = await fetch(`${MIDDLEWARE_API_URL}/api/production/jobs`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(req.body)
        });

        await relayJsonResponse(response, res);
    } catch (error) {
        console.error('Production job queue proxy failed:', error);
        res.status(502).json({
            message: 'Failed to reach middleware production job queue endpoint.',
            details: error.message
        });
    }
});
'@

$serverPath = Join-Path $hmiRoot 'backend\server.cjs'
$serverContent = Get-Content -Path $serverPath -Raw
if (-not $serverContent.Contains("app.get('/api/production/designs'")) {
    $marker = "app.get('/api/system-status', async (req, res) => {"
    if (-not $serverContent.Contains($marker)) {
        throw "Unable to locate production proxy insertion marker in server.cjs"
    }

    $updatedServer = $serverContent.Replace($marker, $proxyAppend + "`r`n" + $marker)
    Set-Content -Path $serverPath -Value $updatedServer -Encoding UTF8
}

Replace-Once -Path (Join-Path $hmiRoot 'components\ProductPage.jsx') `
    -OldValue 'import Header from "./Header";' `
    -NewValue "import Header from `"./Header`";`r`nimport ProductionDesignPanel from './ProductionDesignPanel';"

Replace-Once -Path (Join-Path $hmiRoot 'components\ProductPage.jsx') `
    -OldValue "                {config.ontologyIdentifier === 'prosthesis' && (" `
    -NewValue "                <div className=`"right-column`">`r`n                    <ProductionDesignPanel`r`n                        config={config}`r`n                        username={username}`r`n                        individual={individual}`r`n                        displayedProcessChain={displayedProcessChain}`r`n                        processParameters={processParameters}`r`n                    />`r`n                </div>`r`n                {config.ontologyIdentifier === 'prosthesis' && ("

Write-Output 'HMI production flow update prepared.'
